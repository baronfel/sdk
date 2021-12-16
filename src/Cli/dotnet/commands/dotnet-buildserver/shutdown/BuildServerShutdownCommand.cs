// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.CommandLine.Parsing;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.BuildServer;
using Microsoft.DotNet.Cli;
using Microsoft.DotNet.Cli.Utils;

namespace Microsoft.DotNet.Tools.BuildServer.Shutdown
{   
    record class BuildServerShutdownOptions(ServerEnumerationFlags serversToShutdown, IBuildServerProvider serverProvider, bool useOrderedWait, IReporter reporter);

    internal class BuildServerShutdownCommand : CommandBase
    {
        public BuildServerShutdownOptions _options { get; }

        public BuildServerShutdownCommand(BuildServerShutdownOptions options)
        {
            _options = options;
        }

        public override Task<int> Execute()
        {
            var tasks = StartShutdown();

            if (tasks.Count == 0)
            {
                _options.reporter.WriteLine(LocalizableStrings.NoServersToShutdown.Green());
                return Task.FromResult(0);
            }

            bool success = true;
            while (tasks.Count > 0)
            {
                var index = WaitForResult(tasks.Select(t => t.Item2).ToArray());
                var (server, task) = tasks[index];

                if (task.IsFaulted)
                {
                    success = false;
                    WriteFailureMessage(server, task.Exception);
                }
                else
                {
                    WriteSuccessMessage(server);
                }

                tasks.RemoveAt(index);
            }

            return Task.FromResult(success ? 0 : 1);
        }

        private List<(IBuildServer, Task)> StartShutdown()
        {
            var tasks = new List<(IBuildServer, Task)>();
            foreach (var server in _options.serverProvider.EnumerateBuildServers(_options.serversToShutdown))
            {
                WriteShutdownMessage(server);
                tasks.Add((server, Task.Run(() => server.Shutdown())));
            }

            return tasks;
        }

        private int WaitForResult(Task[] tasks)
        {
            if (_options.useOrderedWait)
            {
                return Task.WaitAny(new [] {tasks.First()});
            }
            return Task.WaitAny(tasks);
        }

        private void WriteShutdownMessage(IBuildServer server)
        {
            if (server.ProcessId != 0)
            {
                _options.reporter.WriteLine(
                    string.Format(
                        LocalizableStrings.ShuttingDownServerWithPid,
                        server.Name,
                        server.ProcessId));
            }
            else
            {
                _options.reporter.WriteLine(
                    string.Format(
                        LocalizableStrings.ShuttingDownServer,
                        server.Name));
            }
        }

        private void WriteFailureMessage(IBuildServer server, AggregateException exception)
        {
            if (server.ProcessId != 0)
            {
                _options.reporter.WriteLine(
                    string.Format(
                        LocalizableStrings.ShutDownFailedWithPid,
                        server.Name,
                        server.ProcessId,
                        exception.InnerException.Message).Red());
            }
            else
            {
                _options.reporter.WriteLine(
                    string.Format(
                        LocalizableStrings.ShutDownFailed,
                        server.Name,
                        exception.InnerException.Message).Red());
            }

            if (Reporter.IsVerbose)
            {
                Reporter.Verbose.WriteLine(exception.ToString().Red());
            }
        }

        private void WriteSuccessMessage(IBuildServer server)
        {
            if (server.ProcessId != 0)
            {
                _options.reporter.WriteLine(
                    string.Format(
                        LocalizableStrings.ShutDownSucceededWithPid,
                        server.Name,
                        server.ProcessId).Green());
            }
            else
            {
                _options.reporter.WriteLine(
                    string.Format(
                        LocalizableStrings.ShutDownSucceeded,
                        server.Name).Green());
            }
        }
    }
}
