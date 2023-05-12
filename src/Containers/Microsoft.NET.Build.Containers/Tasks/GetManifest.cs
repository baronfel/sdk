// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Framework;
using Microsoft.NET.Build.Containers.Resources;
using System.Net.Http.Json;
using Microsoft.Build.Utilities;
using System.Text.Json;
#if NETFRAMEWORK
using System.Linq;
#endif

namespace Microsoft.NET.Build.Containers.Tasks;

public sealed class GetManifest : Microsoft.Build.Utilities.Task, ICancelableTask
{
    private const int FirstVersionWithNewTaggingScheme = 8;

    [Required]
    public string BaseRegistry { get; set; }

    [Required]
    public string BaseRepository { get; set; }

    [Required]
    public string BaseTag { get; set; }

    [Required]
    public string StoragePath { get; set; }

    [Output]
    public ITaskItem? ManifestList { get; set; }

    [Output]
    public ITaskItem[] Manifests { get; set; }

    private readonly CancellationTokenSource _cts = new();


    public GetManifest()
    {
        BaseRegistry = "";
        BaseRepository = "";
        BaseTag = "";
        StoragePath = "";
        ManifestList = null!;
        Manifests = Array.Empty<ITaskItem>();
    }

    public override bool Execute()
    {
        return ExecuteAsync(_cts.Token).GetAwaiter().GetResult();
    }

    internal async Task<bool> ExecuteAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
        Registry reg = new(ContainerHelpers.TryExpandRegistryToUri(BaseRegistry));
        var manifest = await reg.GetManifestAsync(BaseRepository, BaseTag, token);
        var manifestName = $"{BaseRepository.Replace('/', '.')}.{BaseTag}";
        return manifest.Content.Headers.ContentType?.MediaType switch
        {
            Registry.DockerManifestV2 => await DownloadManifestAndReturn(manifestName, await manifest.Content.ReadFromJsonAsync<ManifestV2>(cancellationToken: token), token),
            Registry.DockerManifestListV2 => await DownloadChildManifestsAndReturn(manifestName, await manifest.Content.ReadFromJsonAsync<ManifestListV2>(cancellationToken: token), token),
            var unknownMediaType => LogUnknownTypeErrorAndReturnFalse(unknownMediaType),
        };

        async Task<bool> DownloadManifestAndReturn(string name, ManifestV2 manifest, CancellationToken token)
        {
            var output = Path.Combine(StoragePath, $"{name}.manifest.json");
            await System.IO.File.WriteAllTextAsync(output, JsonSerializer.Serialize(manifest), token);
            var item = new TaskItem(output);
            item.SetMetadata("digest", manifest.GetDigest());
            Manifests = new[] { item };
            ManifestList = null;
            return true;
        }

        bool LogUnknownTypeErrorAndReturnFalse(string? unknownMediaType)
        {
            Log.LogError(Strings.UnknownMediaType, unknownMediaType);
            return false;
        }

        async Task<bool> DownloadChildManifestsAndReturn(string baseName, ManifestListV2 manifestList, CancellationToken token)
        {
            var items = await System.Threading.Tasks.Task.WhenAll(manifestList.manifests.Select(async manifest =>
            {
                var fileName = FileNameFor(manifest);
                var output = Path.Combine(StoragePath, $"{baseName}.{fileName}");
                await System.IO.File.WriteAllTextAsync(output, JsonSerializer.Serialize(manifest), token);
                var item = new TaskItem(output);
                item.SetMetadata("digest", manifest.digest);
                item.SetMetadata("PlatformArch", manifest.platform.architecture);
                item.SetMetadata("PlatformOs", manifest.platform.os);
                item.SetMetadata("PlatformVariant", manifest.platform.variant);
                return item;
            }));
            var output = Path.Combine(StoragePath, $"{baseName}.manifest.json");
            await System.IO.File.WriteAllTextAsync(output, JsonSerializer.Serialize(manifestList), token);
            ManifestList = new TaskItem(output);
            Manifests = items.ToArray();
            return true;
        }

        string FileNameFor(PlatformSpecificManifest p)
        {
            var baseName = $"{p.platform.os}.{p.platform.architecture}";
            if (p.platform.variant is not null)
            {
                baseName += $".{p.platform.variant}";
            }
            return $"{baseName}.manifest.json";
        }
    }

    public void Cancel()
    {
        _cts.Cancel();
    }
}
