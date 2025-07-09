// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.CommandLine;
using System.CommandLine.Completions;
using System.CommandLine.Invocation;
using System.Reflection;
using Microsoft.DotNet.Cli.Commands.Dnx;
using Microsoft.DotNet.Cli.Commands.NuGet;
using Microsoft.DotNet.Cli.Commands.Tool;
using Microsoft.DotNet.Cli.Extensions;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Cli.Utils.Extensions;
using Microsoft.TemplateEngine.Cli;
using Microsoft.TemplateEngine.Cli.Help;
using Command = System.CommandLine.Command;

namespace Microsoft.DotNet.Cli;

public static class Parser
{
    public static readonly RootCommand RootCommand = new()
    {
        Directives = { new DiagramDirective(), new SuggestDirective(), new EnvironmentVariablesDirective() }
    };

    // Subcommands
    public static readonly Command[] Subcommands =
    [
        DnxCommandParser.GetCommand(),
        ToolCommandParser.GetCommand(),
        new System.CommandLine.StaticCompletions.CompletionsCommand()
    ];

    public static readonly Option<bool> DiagOption = CommonOptionsFactory.CreateDiagnosticsOption(recursive: false);

    public static readonly Option<bool> VersionOption = new("--version")
    {
        Arity = ArgumentArity.Zero,
        Action = new PrintVersionAction()
    };

    internal class PrintVersionAction : System.CommandLine.Invocation.SynchronousCommandLineAction
    {
        public PrintVersionAction()
        {
            Terminating = true;
        }
        public override int Invoke(ParseResult parseResult)
        {
            CommandLineInfo.PrintVersion();
            return 0;
        }
    }

    public static readonly Option<bool> InfoOption = new("--info")
    {
        Arity = ArgumentArity.Zero,
        Action = new PrintInfoAction()
    };

    internal class PrintInfoAction : System.CommandLine.Invocation.SynchronousCommandLineAction
    {
        public PrintInfoAction()
        {
            Terminating = true;
        }

        public override int Invoke(ParseResult parseResult)
        {
            CommandLineInfo.PrintInfo();
            return 0;
        }
    }

    public static readonly Option<bool> ListSdksOption = new("--list-sdks")
    {
        Arity = ArgumentArity.Zero
    };

    public static readonly Option<bool> ListRuntimesOption = new("--list-runtimes")
    {
        Arity = ArgumentArity.Zero
    };

    public static readonly Option<bool> CliSchemaOption = new("--cli-schema")
    {
        Description = CliStrings.SDKSchemaCommandDefinition,
        Arity = ArgumentArity.Zero,
        Recursive = true,
        Hidden = true,
        Action = new PrintCliSchemaAction()
    };

    // Argument
    public static readonly Argument<string> DotnetSubCommand = new("subcommand") { Arity = ArgumentArity.ZeroOrOne, Hidden = true };

    private static Command ConfigureCommandLine(RootCommand rootCommand)
    {
        for (int i = rootCommand.Options.Count - 1; i >= 0; i--)
        {
            Option option = rootCommand.Options[i];

            if (option is VersionOption)
            {
                rootCommand.Options.RemoveAt(i);
            }
            else if (option is System.CommandLine.Help.HelpOption helpOption)
            {
                helpOption.Action = new DotnetHelpAction()
                {
                    Builder = DotnetHelpBuilder.Instance.Value
                };

                option.Description = CliStrings.ShowHelpDescription;
            }
        }

        // Add subcommands
        foreach (var subcommand in Subcommands)
        {
            rootCommand.Subcommands.Add(subcommand);
        }

        // Add options
        rootCommand.Options.Add(DiagOption);
        rootCommand.Options.Add(VersionOption);
        rootCommand.Options.Add(InfoOption);
        rootCommand.Options.Add(ListSdksOption);
        rootCommand.Options.Add(ListRuntimesOption);
        rootCommand.Options.Add(CliSchemaOption);

        // Add argument
        rootCommand.Arguments.Add(DotnetSubCommand);

        // NuGet implements several commands in its own repo. Add them to the .NET SDK via the provided API.
        NuGet.CommandLine.XPlat.NuGetCommands.Add(rootCommand);

        rootCommand.SetAction(parseResult =>
        {
            if (parseResult.GetValue(DiagOption) && parseResult.Tokens.Count == 1)
            {
                // when user does not specify any args except of diagnostics ("dotnet -d"), we do nothing
                // as Program.ProcessArgs already enabled the diagnostic output
                return 0;
            }
            else
            {
                // when user does not specify any args (just "dotnet"), a usage needs to be printed
                parseResult.Configuration.Output.WriteLine(CliUsage.HelpText);
                return 0;
            }
        });

        return rootCommand;
    }

    public static Command GetBuiltInCommand(string commandName) =>
        Subcommands.FirstOrDefault(c => c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Implements token-per-line response file handling for the CLI. We use this instead of the built-in S.CL handling
    /// to ensure backwards-compatibility with MSBuild.
    /// </summary>
    public static bool TokenPerLine(string tokenToReplace, out IReadOnlyList<string> replacementTokens, out string errorMessage)
    {
        var filePath = Path.GetFullPath(tokenToReplace);
        if (File.Exists(filePath))
        {
            var lines = File.ReadAllLines(filePath);
            var trimmedLines =
                lines
                    // Remove content in the lines that start with # after trimmer leading whitespace
                    .Select(line => line.TrimStart().StartsWith('#') ? string.Empty : line)
                    // trim leading/trailing whitespace to not pass along dead spaces
                    .Select(x => x.Trim())
                    // Remove empty lines
                    .Where(line => line.Length > 0);
            replacementTokens = [.. trimmedLines];
            errorMessage = null;
            return true;
        }
        else
        {
            replacementTokens = null;
            errorMessage = string.Format(CliStrings.ResponseFileNotFound, tokenToReplace);
            return false;
        }
    }

    public static CommandLineConfiguration Instance { get; } = new(ConfigureCommandLine(RootCommand))
    {
        EnableDefaultExceptionHandler = false,
        EnablePosixBundling = false,
        ResponseFileTokenReplacer = TokenPerLine
    };

    internal static int ExceptionHandler(Exception exception, ParseResult parseResult)
    {
        if (exception is TargetInvocationException)
        {
            exception = exception.InnerException;
        }

        if (exception is GracefulException)
        {
            Reporter.Error.WriteLine(CommandLoggingContext.IsVerbose ?
                exception.ToString().Red().Bold() :
                exception.Message.Red().Bold());
        }
        else if (exception is CommandParsingException)
        {
            Reporter.Error.WriteLine(CommandLoggingContext.IsVerbose ?
                exception.ToString().Red().Bold() :
                exception.Message.Red().Bold());
            parseResult.ShowHelp();
        }
        else if (exception.GetType().Name.Equals("WorkloadManifestCompositionException"))
        {
            Reporter.Error.WriteLine(CommandLoggingContext.IsVerbose ?
                exception.ToString().Red().Bold() :
                exception.Message.Red().Bold());
        }
        else
        {
            Reporter.Error.Write("Unhandled exception: ".Red().Bold());
            Reporter.Error.WriteLine(CommandLoggingContext.IsVerbose ?
                exception.ToString().Red().Bold() :
                exception.Message.Red().Bold());
        }

        System.Diagnostics.Activity.Current?.AddException(exception);

        return 1;
    }

    internal class DotnetHelpBuilder : HelpBuilder
    {
        private DotnetHelpBuilder(int maxWidth = int.MaxValue) : base(maxWidth) { }

        public static Lazy<HelpBuilder> Instance = new(() =>
        {
            int windowWidth;
            try
            {
                windowWidth = Console.WindowWidth;
            }
            catch
            {
                windowWidth = int.MaxValue;
            }

            DotnetHelpBuilder dotnetHelpBuilder = new(windowWidth);

            return dotnetHelpBuilder;
        });

        public static void additionalOption(HelpContext context)
        {
            List<TwoColumnHelpRow> options = [];
            HashSet<Option> uniqueOptions = [];
            foreach (Option option in context.Command.Options)
            {
                if (!option.Hidden && uniqueOptions.Add(option))
                {
                    options.Add(context.HelpBuilder.GetTwoColumnRow(option, context));
                }
            }

            if (options.Count <= 0)
            {
                return;
            }

            context.Output.WriteLine(CliStrings.MSBuildAdditionalOptionTitle);
            context.HelpBuilder.WriteColumns(options, context);
            context.Output.WriteLine();
        }

        public override void Write(HelpContext context)
        {
            var command = context.Command;
            var helpArgs = new string[] { "--help" };

            // custom help overrides
            if (command.Equals(RootCommand))
            {
                Console.Out.WriteLine(CliUsage.HelpText);
                return;
            }

            // argument/option cleanups specific to help
            foreach (var option in command.Options)
            {
                option.EnsureHelpName();
            }

            if (command.Equals(NuGetCommandParser.GetCommand()) || command.Parents.Any(parent => parent == NuGetCommandParser.GetCommand()))
            {
                NuGetCommand.Run(context.ParseResult);
            }
            else if (command is TemplateEngine.Cli.Commands.ICustomHelp helpCommand)
            {
                var blocks = helpCommand.CustomHelpLayout();
                foreach (var block in blocks)
                {
                    block(context);
                }
            }
            else
            {
                base.Write(context);
            }
        }
    }

    private class PrintCliSchemaAction : SynchronousCommandLineAction
    {
        internal PrintCliSchemaAction()
        {
            Terminating = true;
        }
        public override int Invoke(ParseResult parseResult)
        {
            CliSchema.PrintCliSchema(parseResult.CommandResult, parseResult.Configuration.Output, Program.TelemetryClient);
            return 0;
        }
    }
}
