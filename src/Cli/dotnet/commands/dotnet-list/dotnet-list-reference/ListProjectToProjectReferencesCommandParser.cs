// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Microsoft.DotNet.Tools.List.ProjectToProjectReferences;
using LocalizableStrings = Microsoft.DotNet.Tools.List.ProjectToProjectReferences.LocalizableStrings;

namespace Microsoft.DotNet.Cli
{
    internal static class ListProjectToProjectReferencesCommandParser
    {
        public static readonly Argument Argument = new Argument<string>("argument") { Arity = ArgumentArity.ZeroOrOne, IsHidden = true };

        private static readonly Command Command = ConstructCommand();

        public static Command GetCommand()
        {
            return Command;
        }

        private static Command ConstructCommand()
        {
            var command = new Command("reference", LocalizableStrings.AppFullName);

            command.AddArgument(Argument);

            command.Handler = CommandHandler.Create<ParseResult>((parseResult) => new ListProjectToProjectReferencesCommand(parseResult).Execute());

            return command;
        }
    }
}
