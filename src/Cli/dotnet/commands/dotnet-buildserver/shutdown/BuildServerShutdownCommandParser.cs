// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Microsoft.DotNet.BuildServer;
using Microsoft.DotNet.Tools.BuildServer.Shutdown;
using LocalizableStrings = Microsoft.DotNet.Tools.BuildServer.Shutdown.LocalizableStrings;

namespace Microsoft.DotNet.Cli
{
    internal static class ServerShutdownCommandParser
    {
        public static readonly Option<bool> MSBuildOption = new Option<bool>("--msbuild", LocalizableStrings.MSBuildOptionDescription);
        public static readonly Option<bool> VbcsOption = new Option<bool>("--vbcscompiler", LocalizableStrings.VBCSCompilerOptionDescription);
        public static readonly Option<bool> RazorOption = new Option<bool>("--razor", LocalizableStrings.RazorOptionDescription);

        private static readonly Command Command = ConstructCommand();

        public static Command GetCommand()
        {
            return Command;
        }

        internal class BuildServerShutdownOptionsBinder : BinderBase<BuildServerShutdownOptions>
        {
            private readonly Option<bool> _msbuild;
            private readonly Option<bool> _vbcs;
            private readonly Option<bool> _razor;
            private readonly Utils.IReporter _outputReporter;
            private readonly Utils.IReporter _errorReporter;

            public BuildServerShutdownOptionsBinder(Option<bool> msbuild, Option<bool> vbcs, Option<bool> razor, Microsoft.DotNet.Cli.Utils.IReporter outputReporter, Microsoft.DotNet.Cli.Utils.IReporter errorReporter)
            {
                _msbuild = msbuild;
                _vbcs = vbcs;
                _razor = razor;
                _outputReporter = outputReporter;
                _errorReporter = errorReporter;
            }

            protected override BuildServerShutdownOptions GetBoundValue(BindingContext bindingContext) {
                var result = bindingContext.ParseResult;
                bool msbuild = result.GetValueForOption(_msbuild);
                bool vbcscompiler = result.GetValueForOption(_vbcs);
                bool razor = result.GetValueForOption(_razor);
                bool all = !msbuild && !vbcscompiler && !razor;

                var enumerationFlags = ServerEnumerationFlags.None;
                if (msbuild || all)
                {
                    enumerationFlags |= ServerEnumerationFlags.MSBuild;
                }

                if (vbcscompiler || all)
                {
                    enumerationFlags |= ServerEnumerationFlags.VBCSCompiler;
                }

                if (razor || all)
                {
                    enumerationFlags |= ServerEnumerationFlags.Razor;
                }
                return new BuildServerShutdownOptions(enumerationFlags, new BuildServerProvider(), false, _outputReporter);
            }
        }

        private static Command ConstructCommand()
        {
            var command = new Command("shutdown", LocalizableStrings.CommandDescription);

            command.AddOption(MSBuildOption);
            command.AddOption(VbcsOption);
            command.AddOption(RazorOption);
            var binder = new BuildServerShutdownOptionsBinder(MSBuildOption, VbcsOption, RazorOption, Utils.Reporter.Output, Utils.Reporter.Error);
            command.SetHandler((BuildServerShutdownOptions options) => new BuildServerShutdownCommand(options).Execute(), binder);

            return command;
        }
    }
}
