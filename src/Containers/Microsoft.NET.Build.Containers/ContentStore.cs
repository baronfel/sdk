// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.NET.Build.Containers.Resources;

namespace Microsoft.NET.Build.Containers;

internal static class ContentStore
{
    public static string ArtifactRoot { get; set; } = Path.Combine(Path.GetTempPath(), "Containers");
    public static string ContentRoot
    {
        get
        {
            string contentPath = Path.Join(ArtifactRoot, "Content");

            Directory.CreateDirectory(contentPath);

            return contentPath;
        }
    }

    public static string TempPath
    {
        get
        {
            string tempPath = Path.Join(ArtifactRoot, "Temp");

            Directory.CreateDirectory(tempPath);

            return tempPath;
        }
    }

    /// <summary>
    /// Calculates the path for a descriptor in the blob content store
    /// </summary>
    /// <param name="descriptor"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static string PathForDescriptor(Descriptor descriptor)
    {
        string digest = descriptor.Digest;

        Debug.Assert(digest.StartsWith("sha256:", StringComparison.Ordinal));

        string contentHash = digest.Substring("sha256:".Length);

        string extension = ExtensionForMediaType(descriptor.MediaType);

        return GetPathForHash(contentHash) + extension;
    }

    public static string PathForLayer(ManifestLayer layer)
    {
        string digest = layer.digest;

        return PathForDigest(digest) + ExtensionForMediaType(layer.mediaType);
    }

    public static string ExtensionForMediaType(string mediaType)
    {
        string extension = mediaType switch
        {
            "application/vnd.docker.image.rootfs.diff.tar.gzip"
            or "application/vnd.oci.image.layer.v1.tar+gzip"
            or "application/vnd.docker.image.rootfs.foreign.diff.tar.gzip"
                => ".tar.gz",
            "application/vnd.docker.image.rootfs.diff.tar"
            or "application/vnd.oci.image.layer.v1.tar"
                => ".tar",
            _ => throw new ArgumentException(Resource.FormatString(nameof(Strings.UnrecognizedMediaType), mediaType))
        };

        return extension;
    }

    public static string PathForDigest(string digest)
    {
        string contentHash = digest.Substring("sha256:".Length);

        return GetPathForHash(contentHash);
    }

    /// <summary>
    /// Provides a stable name for a named repository reference, i.e. not the resolved digest
    /// </summary>
    /// <param name="repositoryName"></param>
    /// <param name="referenceName"></param>
    /// <returns></returns>
    public static string PathForRepositoryReference(string registryName, string repositoryName, string referenceName) => Path.Combine(ContentRoot, "References", registryName, repositoryName, referenceName);

    public static string GetPathForHash(string contentHash)
    {
        return Path.Combine(ContentRoot, contentHash);
    }


    public static string GetTempFile()
    {
        return Path.Join(TempPath, Path.GetRandomFileName());
    }
}
