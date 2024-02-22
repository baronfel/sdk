// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.DotNet.Configurer;
using Microsoft.DotNet.Tools.MSBuild;
using Microsoft.DotNet.Workloads.Workload.Install;

namespace Microsoft.DotNet.Tools
{
    public class RestoringCommand : MSBuildForwardingApp
    {
        private bool AdvertiseWorkloadUpdates;

        public RestoringCommand(
            IEnumerable<string> msbuildArgs,
            bool noRestore,
            string msbuildPath = null,
            string userProfileDir = null,
            bool advertiseWorkloadUpdates = true)
            : base(GetCommandArguments(msbuildArgs, noRestore), msbuildPath)
        {
            userProfileDir = CliFolderPathCalculator.DotnetUserProfileFolderPath;
            Task.Run(() => WorkloadManifestUpdater.BackgroundUpdateAdvertisingManifestsAsync(userProfileDir));
            AdvertiseWorkloadUpdates = advertiseWorkloadUpdates;

            if (!noRestore)
            {
                NuGetSignatureVerificationEnabler.ConditionallyEnable(this);
            }
        }

        private static IEnumerable<string> GetCommandArguments(
            IEnumerable<string> arguments,
            bool noRestore)
        {
            if (noRestore)
            {
                return arguments;
            }

            return Prepend("-restore", arguments);
        }

        private static IEnumerable<string> Prepend(string arguments, IEnumerable<string> otherArgs)
            => new[] { arguments }.Concat(otherArgs);

        public override int Execute()
        {
            int exitCode = base.Execute();
            if (AdvertiseWorkloadUpdates)
            {
                WorkloadManifestUpdater.AdvertiseWorkloadUpdates();
            }
            return exitCode;
        }
    }
}
