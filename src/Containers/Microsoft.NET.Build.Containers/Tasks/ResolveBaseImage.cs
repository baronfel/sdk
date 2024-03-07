using System.Xml.Serialization;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.NET.Build.Containers.Logging;
using Microsoft.NET.Build.Containers.Resources;

namespace Microsoft.NET.Build.Containers.Tasks;

///<summary>
/// This task is given a fully-qualified base image and it resolves the best base image for a given TFM/Runtime/etc.
///</summary>
public sealed class ResolveBaseImage : Microsoft.Build.Utilities.Task, ICancelableTask
{
    [Required]
    public string BaseImageReference { get; private set; }

    [Required]
    public string RuntimeIdentifier { get; private set; }

    [Required]
    public string RuntimeIdentifierGraphPath { get; private set; }

    [Output]
    public ITaskItem BaseContainerManifest { get; private set; }

    [Output]
    public ITaskItem BaseContainerConfig { get; private set; }

    [Output]
    public ITaskItem[] BaseImageLayers { get; private set; }

    private readonly CancellationTokenSource _cts;

    public ResolveBaseImage()
    {
        BaseImageReference = "";
        RuntimeIdentifier = "";
        RuntimeIdentifierGraphPath = "";

        BaseContainerManifest = null!;
        BaseContainerConfig = null!;
        BaseImageLayers = Array.Empty<ITaskItem>();
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

        try
        {
            (var manifest, var config) = await registry.GetManifestAndConfig(containerName, reference, RuntimeIdentifier, manifestPicker, _cts.Token).ConfigureAwait(false);
            CreateItemForManifest(manifest);
            CreateItemForConfig(config, manifest.Config.digest);
            CreateItemsForBaseImageLayers(manifest.Layers);
            return true;
        }
        catch (RepositoryNotFoundException)
        {
            Log.LogErrorWithCodeFromResources(nameof(Strings.RepositoryNotFound), containerName, reference, registry.RegistryName);
            return !Log.HasLoggedErrors;
        }
        catch (UnableToAccessRepositoryException)
        {
            Log.LogErrorWithCodeFromResources(nameof(Strings.UnableToAccessRepository), containerName, registry.RegistryName);
            return !Log.HasLoggedErrors;
        }
        catch (ContainerHttpException e)
        {
            Log.LogErrorFromException(e, showStackTrace: false, showDetail: true, file: null);
            return !Log.HasLoggedErrors;
        }

        void CreateItemForManifest(ManifestV2 manifest)
        {
            if (manifest.KnownDigest is not null)
            {
                var path = ContentStore.PathForDigest(manifest.KnownDigest);
                var item = new TaskItem(path, new Dictionary<string, string?>
                {
                    ["Digest"] = manifest.KnownDigest,
                    ["Tag"] = containerTag,
                    ["MediaType"] = manifest.MediaType,
                });

                BaseContainerManifest = item;
            }
        }

        void CreateItemForConfig(ImageConfig config, string configDigest)
        {
            var path = ContentStore.PathForDigest(configDigest);
            var item = new TaskItem(path, new Dictionary<string, string?>
            {
                ["Digest"] = configDigest,
                ["Tag"] = containerTag,
            });

            BaseContainerConfig = item;
        }

        void CreateItemsForBaseImageLayers(List<ManifestLayer> layers)
        {
            // these may not exist - we'll download them later in parallel
            BaseImageLayers = layers.Select(layer =>
            {
                var path = ContentStore.PathForLayer(layer);
                return new TaskItem(path, new Dictionary<string, string?>
                {
                    ["Digest"] = layer.digest,
                    ["Size"] = layer.size.ToString(),
                    ["MediaType"] = layer.mediaType,
                });
            }).ToArray();
        }
    }


    void ICancelableTask.Cancel() => _cts.Cancel();
}
