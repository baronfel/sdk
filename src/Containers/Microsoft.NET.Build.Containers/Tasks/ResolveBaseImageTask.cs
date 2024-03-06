using System.Xml.Serialization;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.NET.Build.Containers.Logging;

namespace Microsoft.NET.Build.Containers.Tasks;

///<summary>
/// This task is given a fully-qualified base image and it resolves the best base image for a given TFM/Runtime/etc.
///</summary>
public sealed class ResolveBaseImage : Microsoft.Build.Utilities.Task, ICancelableTask
{
    [Required]
    public string BaseImageReference { get; set; }

    [Required]
    public string RuntimeIdentifier { get; set; }

    [Required]
    public string RuntimeIdentifierGraphPath { get; set; }

    private readonly CancellationTokenSource _cts;

    public ResolveBaseImage()
    {
        BaseImageReference = "";
        _cts = new CancellationTokenSource();
    }

    public override bool Execute()
    {
        return ExecuteCore().GetAwaiter().GetResult();
    }

    private async Task<bool> ExecuteCore()
    {
        using MSBuildLoggerProvider loggerProvider = new(Log);
        ILoggerFactory msbuildLoggerFactory = new LoggerFactory(new[] { loggerProvider });
        var logger = msbuildLoggerFactory.CreateLogger<CreateNewImage>();

        if (!ContainerHelpers.TryParseFullyQualifiedContainerName(BaseImageReference, out var containerRegistry, out var containerName, out var containerTag, out var containerDigest, out var isRegistrySpecified))
        {
            Log.LogError($"{BaseImageReference} is not a valid fully-qualified container image reference.");
            return false;
        }

        var registry = new Registry(containerRegistry, logger);
        var manifestPicker = new RidGraphManifestPicker(RuntimeIdentifierGraphPath);
        var reference = containerTag ?? containerDigest ?? "latest";
        var image = await registry.GetImageManifestAsync(containerName, reference, RuntimeIdentifier, manifestPicker, _cts.Token);
        CreateItemForImage(image);
        return true;
    }

    private CodeIdentifier CreateItemForImage(ImageBuilder builder)
    {

    }

    void ICancelableTask.Cancel() => _cts.Cancel();
}
