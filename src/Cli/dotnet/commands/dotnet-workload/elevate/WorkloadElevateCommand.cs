// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.CommandLine.Parsing;
using System.Threading.Tasks;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Workloads.Workload.Install;

namespace Microsoft.DotNet.Workloads.Workload.Elevate
{
    internal class WorkloadElevateCommand : CommandBase
    {
        private NetSdkMsiInstallerServer _server;

        public WorkloadElevateCommand(ParseResult parseResult)
        {
        }

        public override Task<int> Execute()
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    _server = NetSdkMsiInstallerServer.Create();
                    _server.Run();
                }
                catch (Exception e)
                {
                    throw new GracefulException(e.Message, isUserError: false);
                }
                finally
                {
                    _server?.Shutdown();
                }
            }
            else
            {
                throw new GracefulException(LocalizableStrings.RequiresWindows, isUserError: false);
            }

            return Task.FromResult(0);
        }
    }
}
