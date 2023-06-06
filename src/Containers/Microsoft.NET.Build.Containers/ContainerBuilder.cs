// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using Microsoft.NET.Build.Containers.Registry;
using Microsoft.NET.Build.Containers.Resources;

namespace Microsoft.NET.Build.Containers;

public static class ContainerBuilder
{
    public static async Task<int> ContainerizeAsync(
        DirectoryInfo publishDirectory,
        string workingDir,
        string baseRegistry,
        string baseImageName,
        string baseImageTag,
        string[] entrypoint,
        string[]? entrypointArgs,
        string imageName,
        string[] imageTags,
        string? outputRegistry,
        Dictionary<string, string> labels,
        Port[]? exposedPorts,
        Dictionary<string, string> envVars,
        string containerRuntimeIdentifier,
        string ridGraphPath,
        string localContainerDaemon,
        string? containerUser,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        ILogger logger = loggerFactory.CreateLogger("Containerize");
        cancellationToken.ThrowIfCancellationRequested();
        if (!publishDirectory.Exists)
        {
            throw new ArgumentException(string.Format(Resource.GetString(nameof(Strings.PublishDirectoryDoesntExist)), nameof(publishDirectory), publishDirectory.FullName));
        }
        bool isDaemonPull = string.IsNullOrEmpty(baseRegistry);
        RegistryManager? sourceRegistry = isDaemonPull ? null : new RegistryManager(ContainerHelpers.TryExpandRegistryToUri(baseRegistry), logger: logger);
        ImageReference sourceImageReference = new(sourceRegistry, baseImageName, baseImageTag);

        bool isDaemonPush = string.IsNullOrEmpty(outputRegistry);
        RegistryManager? destinationRegistry = isDaemonPush ? null : new RegistryManager(ContainerHelpers.TryExpandRegistryToUri(outputRegistry!), logger: logger);
        IEnumerable<ImageReference> destinationImageReferences = imageTags.Select(t => new ImageReference(destinationRegistry, imageName, t));

        ImageBuilder? imageBuilder;
        if (sourceRegistry is { } registry)
        {
            imageBuilder = await registry.GetImageManifestAsync(
                baseImageName,
                baseImageTag,
                containerRuntimeIdentifier,
                ridGraphPath,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            throw new NotSupportedException(Resource.GetString(nameof(Strings.ImagePullNotSupported)));
        }
        if (imageBuilder is null)
        {
            Console.WriteLine(DiagnosticMessage.ErrorFromResourceWithCode(nameof(Strings.BaseImageNotFound), sourceImageReference.RepositoryAndTag, containerRuntimeIdentifier));
            return 1;
        }
        logger.LogInformation("Building image '{0}' with tags {1} on top of base image {2}", imageName, string.Join(",", imageName), sourceImageReference);
        cancellationToken.ThrowIfCancellationRequested();

        Layer newLayer = Layer.FromDirectory(publishDirectory.FullName, workingDir, imageBuilder.IsWindows);
        imageBuilder.AddLayer(newLayer);
        imageBuilder.SetWorkingDirectory(workingDir);
        imageBuilder.SetEntryPoint(entrypoint, entrypointArgs);
        foreach (KeyValuePair<string, string> label in labels)
        {
            // labels are validated by System.CommandLine API
            imageBuilder.AddLabel(label.Key, label.Value);
        }
        foreach (KeyValuePair<string, string> envVar in envVars)
        {
            imageBuilder.AddEnvironmentVariable(envVar.Key, envVar.Value);
        }
        foreach ((int number, PortType type) in exposedPorts ?? Array.Empty<Port>())
        {
            // ports are validated by System.CommandLine API
            imageBuilder.ExposePort(number, type);
        }
        if (containerUser is { } user)
        {
            imageBuilder.SetUser(user);
        }
        BuiltImage builtImage = imageBuilder.Build();
        cancellationToken.ThrowIfCancellationRequested();

        foreach (ImageReference destinationImageReference in destinationImageReferences)
        {
            if (isDaemonPush)
            {
                LocalDocker localDaemon = GetLocalDaemon(localContainerDaemon, logger);
                if (!(await localDaemon.IsAvailableAsync(cancellationToken).ConfigureAwait(false)))
                {
                    Console.WriteLine(DiagnosticMessage.ErrorFromResourceWithCode(nameof(Strings.LocalDaemonNotAvailable)));
                    return 7;
                }

                try
                {
                    await localDaemon.LoadAsync(builtImage, sourceImageReference, destinationImageReference, cancellationToken).ConfigureAwait(false);
                    logger.LogInformation("Pushed container '{0}' to Docker daemon", destinationImageReference.RepositoryAndTag);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(DiagnosticMessage.ErrorFromResourceWithCode(nameof(Strings.RegistryOutputPushFailed), ex.Message));
                    return 1;
                }
            }
            else
            {
                try
                {
                    if (destinationImageReference.Registry is not null)
                    {
                        await (destinationImageReference.Registry.PushAsync(
                            builtImage,
                            sourceImageReference,
                            destinationImageReference,
                            cancellationToken)).ConfigureAwait(false);
                        logger.LogInformation("Pushed container '{0}' to registry '{1}'", destinationImageReference.RepositoryAndTag, destinationImageReference.Registry.RegistryName);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(DiagnosticMessage.ErrorFromResourceWithCode(nameof(Strings.RegistryOutputPushFailed), e.Message));
                    return 1;
                }
            }
        }
        return 0;
    }

    private static LocalDocker GetLocalDaemon(string localDaemonType, ILogger logger)
    {
        LocalDocker daemon = localDaemonType switch
        {
            KnownDaemonTypes.Docker => new LocalDocker(logger),
            _ => throw new ArgumentException(Resource.FormatString(nameof(Strings.UnknownDaemonType), localDaemonType, String.Join(",", KnownDaemonTypes.SupportedLocalDaemonTypes)), nameof(localDaemonType))
        };
        return daemon;
    }
}
