using System.Xml.Serialization;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.NET.Build.Containers.Logging;

namespace Microsoft.NET.Build.Containers.Tasks;

///<summary>
///</summary>
public sealed class CreateAppLayer : Microsoft.Build.Utilities.Task, ICancelableTask
{
    [Required]
    public string FileRoot { get; set; }
    [Required]
    public ITaskItem[] Files { get; set; }

    [Required]
    public string WorkingDirectory { get; set; }

    [Required]
    public bool IsWindows { get; set; }

    [Required]
    public ITaskItem Manifest { get; set; }

    [Required]
    public string OutputLocation { get; set; }

    [Output]
    public ITaskItem OutputLayer { get; set; }

    private readonly CancellationTokenSource _cts;

    public CreateAppLayer()
    {
        Files = Array.Empty<ITaskItem>();
        WorkingDirectory = string.Empty;
        Manifest = null!;
        OutputLocation = string.Empty;
        FileRoot = string.Empty;
        OutputLayer = null!;
        _cts = new CancellationTokenSource();
    }

    public override bool Execute()
    {
        var files = Files.Select(f => new FileInfo(f.ItemSpec));
        var layer = Layer.FromFiles(FileRoot, files, WorkingDirectory, IsWindows, Manifest.GetMetadata("MediaType"), _cts.Token);
        // ensure the holding directory exists
        new FileInfo(OutputLocation).Directory?.Create();
        File.Copy(ContentStore.PathForDescriptor(layer.Descriptor), OutputLocation, overwrite: true);
        OutputLayer = new TaskItem(OutputLocation, new Dictionary<string, string>
        {
            ["Digest"] = layer.Descriptor.Digest,
            ["Size"] = layer.Descriptor.Size.ToString(),
            ["MediaType"] = layer.Descriptor.MediaType
        });
        return true;
    }

    void ICancelableTask.Cancel() => _cts.Cancel();
}
