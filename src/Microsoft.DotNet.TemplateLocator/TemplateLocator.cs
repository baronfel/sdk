// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.DotNetSdkResolver;
using Microsoft.DotNet.NativeWrapper;

namespace Microsoft.DotNet.TemplateLocator
{
    public sealed class TemplateLocator
    {
        private readonly Lazy<NETCoreSdkResolver> _netCoreSdkResolver;
        private readonly Func<string, string?> _getEnvironmentVariable;
        private readonly Func<string>? _getCurrentProcessPath;

        public TemplateLocator()
            : this(Environment.GetEnvironmentVariable, null, VSSettings.Ambient)
        {
        }

        /// <summary>
        /// Test constructor
        /// </summary>
        public TemplateLocator(Func<string, string?> getEnvironmentVariable, Func<string>? getCurrentProcessPath, VSSettings vsSettings)
        {
            _netCoreSdkResolver =
                new Lazy<NETCoreSdkResolver>(() => new NETCoreSdkResolver(getEnvironmentVariable, vsSettings));

            _getEnvironmentVariable = getEnvironmentVariable;
            _getCurrentProcessPath = getCurrentProcessPath;
        }

        public IReadOnlyCollection<IOptionalSdkTemplatePackageInfo> GetDotnetSdkTemplatePackages(
            string sdkVersion,
            string dotnetRootPath,
            string? userProfileDir)
        {
            if (string.IsNullOrWhiteSpace(sdkVersion))
            {
                throw new ArgumentException($"'{nameof(sdkVersion)}' cannot be null or whitespace", nameof(sdkVersion));
            }

            if (string.IsNullOrWhiteSpace(dotnetRootPath))
            {
                throw new ArgumentException($"'{nameof(dotnetRootPath)}' cannot be null or whitespace",
                    nameof(dotnetRootPath));
            }

            //  Will the current directory correspond to the folder we are creating a project in?  If we need
            //  to honor global.json workload version selection for template creation in Visual Studio, we may
            //  need to update this interface to pass a folder where we should start the search for global.jso
            return [];
        }

        public bool TryGetDotnetSdkVersionUsedInVs(string vsVersion, out string? sdkVersion)
        {
            string? dotnetExeDir = EnvironmentProvider.GetDotnetExeDirectory(_getEnvironmentVariable, _getCurrentProcessPath);

            if (!Version.TryParse(vsVersion, out var parsedVsVersion))
            {
                throw new ArgumentException(vsVersion + " is not a valid version");
            }

            // VS major minor version will match msbuild major minor
            // and for resolve SDK, major minor version is enough
            var msbuildMajorMinorVersion = new Version(parsedVsVersion.Major, parsedVsVersion.Minor, 0);

            var resolverResult =
                _netCoreSdkResolver.Value.ResolveNETCoreSdkDirectory(null, msbuildMajorMinorVersion, true,
                    dotnetExeDir);

            if (resolverResult.ResolvedSdkDirectory == null)
            {
                sdkVersion = null;
                return false;
            }
            else
            {
                sdkVersion = new DirectoryInfo(resolverResult.ResolvedSdkDirectory).Name;
                return true;
            }
        }

        private class OptionalSdkTemplatePackageInfo : IOptionalSdkTemplatePackageInfo
        {
            public OptionalSdkTemplatePackageInfo(string templatePackageId, string templateVersion, string path)
            {
                TemplatePackageId = templatePackageId ?? throw new ArgumentNullException(nameof(templatePackageId));
                TemplateVersion = templateVersion ?? throw new ArgumentNullException(nameof(templateVersion));
                Path = path ?? throw new ArgumentNullException(nameof(path));
            }

            public string TemplatePackageId { get; }
            public string TemplateVersion { get; }
            public string Path { get; }
        }
    }
}
