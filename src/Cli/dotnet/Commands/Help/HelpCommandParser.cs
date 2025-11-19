// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using Microsoft.DotNet.Cli.CommandLine;

namespace Microsoft.DotNet.Cli.Commands.Help;

internal static class HelpCommandParser
{
    public static readonly string DocsLink = "https://aka.ms/dotnet-help";

    public static readonly Argument<string[]> Argument =
    new Argument<string[]>(CliCommandStrings.CommandArgumentName)
    {
        Description = CliCommandStrings.CommandArgumentDescription,
        Arity = ArgumentArity.ZeroOrMore
    }
    .ReportInTelemetry(args => args is string[] argv ? string.Join(",", argv.Select(Utils.Sha256Hasher.HashWithNormalizedCasing)) : string.Empty);

    private static readonly Command Command = ConstructCommand();

    public static Command GetCommand()
    {
        return Command;
    }

    private static Command ConstructCommand()
    {
        Command command = new("help", CliCommandStrings.HelpAppFullName)
        {
            DocsLink = DocsLink
        };

        command.Arguments.Add(Argument);

        command.SetAction(HelpCommand.Run);

        return command;
    }
}

