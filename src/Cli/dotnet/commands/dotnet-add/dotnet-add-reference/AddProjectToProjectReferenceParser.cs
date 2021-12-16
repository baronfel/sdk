// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Linq;
using Microsoft.DotNet.Tools.Add.ProjectToProjectReference;
using LocalizableStrings = Microsoft.DotNet.Tools.Add.ProjectToProjectReference.LocalizableStrings;

namespace Microsoft.DotNet.Cli
{
    internal static class AddProjectToProjectReferenceParser
    {
        public static readonly Argument<IEnumerable<string>> ProjectPathArgument = new Argument<IEnumerable<string>>(LocalizableStrings.ProjectPathArgumentName)
        {
            Description = LocalizableStrings.ProjectPathArgumentDescription,
            Arity = ArgumentArity.OneOrMore
        };

        public static readonly Option<string> FrameworkOption = new Option<string>(new string[] { "-f", "--framework" }, LocalizableStrings.CmdFrameworkDescription)
        {
            ArgumentHelpName = Tools.Add.PackageReference.LocalizableStrings.CmdFramework
                
        }.AddCompletions(Complete.TargetFrameworksFromProjectFile);

        public static readonly Option<bool> InteractiveOption = CommonOptions.InteractiveOption;

        private static readonly Command Command = ConstructCommand();

        public static Command GetCommand()
        {
            return Command;
        }
        internal class AddProjectToProjectReferenceBinder : BinderBase<AddProjectToProjectReferenceOptions>
        {
            private readonly Argument<string> _fileOrProjectArg;
            private readonly Option<bool> _interactive;
            private readonly Option<string> _framework;
            private readonly Argument<IEnumerable<string>> _projectsArg;

            public AddProjectToProjectReferenceBinder(Argument<string> fileOrProjectArg, Option<bool> interactive, Option<string> framework, Argument<IEnumerable<string>> projectsArg) {
                _fileOrProjectArg = fileOrProjectArg;
                _interactive = interactive;
                _framework = framework;
                _projectsArg = projectsArg;
            }
            protected override AddProjectToProjectReferenceOptions GetBoundValue(BindingContext bindingContext) {
                var sourceProject = bindingContext.ParseResult.GetValueForArgument(_fileOrProjectArg);
                var interactive = bindingContext.ParseResult.GetValueForOption(_interactive);
                var framework = bindingContext.ParseResult.GetValueForOption(_framework);
                var projectsToAdd = bindingContext.ParseResult.GetValueForArgument(_projectsArg).ToArray();

                return new AddProjectToProjectReferenceOptions(sourceProject, interactive, framework, projectsToAdd);
            }
        }
        private static Command ConstructCommand()
        {
            var command = new Command("reference", LocalizableStrings.AppFullName);

            command.AddArgument(ProjectPathArgument);
            command.AddOption(FrameworkOption);
            command.AddOption(InteractiveOption);
            var binder = new AddProjectToProjectReferenceBinder(AddCommandParser.ProjectArgument, InteractiveOption, FrameworkOption, ProjectPathArgument);
            command.SetHandler((AddProjectToProjectReferenceOptions options) => new AddProjectToProjectReferenceCommand(options).Execute(), binder);

            return command;
        }
    }
}
