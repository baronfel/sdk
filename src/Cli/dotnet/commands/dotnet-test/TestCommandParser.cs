// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.IO;
using System.Linq;
using Microsoft.DotNet.Tools;
using Microsoft.DotNet.Tools.Test;
using LocalizableStrings = Microsoft.DotNet.Tools.Test.LocalizableStrings;

namespace Microsoft.DotNet.Cli
{
    internal static class TestCommandParser
    {
        public static readonly string DocsLink = "https://aka.ms/dotnet-test";

        /// <summary>
        /// Parser delegate that only accepts a token that is a 
        /// * project or solution file
        /// * directory that contains a project or solution
        /// * .dll or .exe file
        /// </summary>
        /// <remarks>
        /// S.CL usage note - OnlyTake(0) signals that this token should be returned to the
        /// token stream for the next argument. In this way we prevent initial tokens that 
        /// are not relevant from being captured in this argument, since the syntax is a bit
        /// ambiguous. 
        /// </remarks>
        private static ParseArgument<string> IsProjectOrSln =
            (ctx) =>
            {
                bool HasProjectOrSolution(DirectoryInfo dir) =>
                    dir.EnumerateFiles("*.*proj").Any() || dir.EnumerateFiles(".sln").Any();

                if (ctx.Tokens.Count == 0)
                {
                    ctx.OnlyTake(0);
                    return null;
                }
                else
                {
                    var tokenValue = ctx.Tokens[0].Value;
                    var ext = System.IO.Path.GetExtension(tokenValue);
                    if (ext.EndsWith("proj") || ext.EndsWith(".sln") || ext.EndsWith(".dll") || ext.EndsWith(".exe"))
                    {
                        ctx.OnlyTake(1);
                        return tokenValue;
                    }
                    else
                    {
                        var path = System.IO.Path.GetFullPath(tokenValue);
                        var dir = new System.IO.DirectoryInfo(path);
                        if (dir.Exists && HasProjectOrSolution(dir))
                        {
                            ctx.OnlyTake(1);
                            return tokenValue;
                        }

                        ctx.OnlyTake(0);
                        return null;
                    }
                }
            };

        public static readonly Argument<string> SlnOrProjectArgument = new Argument<string>(CommonLocalizableStrings.SolutionOrProjectArgumentName, parse: IsProjectOrSln)
        {
            Description = CommonLocalizableStrings.SolutionOrProjectArgumentDescription,
            Arity = ArgumentArity.ZeroOrOne
        };

        public static (bool success, string key, string value) TryParseRunSetting (string token) {
            var parts = token.Split('=');
            if (parts.Length == 2) {
                return (true, parts[0], parts[1]);
            }
            return (false, null, null);
        }

        /// <summary>
        /// A parser that takes from the start of the token stream until the the first argument that could be a RunSetting
        /// </summary>
        private static ParseArgument<string[]> ParseUntilFirstPotentialRunSetting = ctx =>
        {
            if (ctx.Tokens.Count == 0)
            {
                ctx.OnlyTake(0);
                return Array.Empty<string>();
            }
            var tokens = new List<string>();
            foreach (var token in ctx.Tokens) {
                var (success, key, value) = TryParseRunSetting(token.Value);
                if (success) {
                    break;
                }
                else 
                {
                    tokens.Add(token.Value);
                }
            }
            ctx.OnlyTake(tokens.Count);
            return tokens.ToArray();
        };

        // TODO(ch): localizable names and descriptions for this
        public static readonly Argument<string[]> ForwardedArgs = new("adapter-args", parse: ParseUntilFirstPotentialRunSetting)
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        private static ParseArgument<(string key, string value)[]> ParseRunSettings = ctx =>
        {
            if (ctx.Tokens.Count == 0)
            {
                ctx.OnlyTake(0);
                return Array.Empty<(string key, string value)>();
            }
            var settings = new List<(string key, string value)>(ctx.Tokens.Count);
            var consumed = 0;
            foreach (var token in ctx.Tokens)
            {
                var parts = token.Value.Split('=', 2);
                if (parts.Length == 2)
                {
                    consumed += 1;
                    settings.Add((parts[0], parts[1]));
                }
                else
                {
                    // TODO(ch): Localizable name for this
                    ctx.ErrorMessage = $"Argument '{token.Value}' could not be parsed as a RunSetting. Use a key/value pair separated with an equals character, like 'foo=bar'";
                    ctx.OnlyTake(consumed);
                    return null;
                }
            }
            ctx.OnlyTake(consumed);
            return settings.ToArray();
        };

        // TODO(ch): localizable names and descriptions for this
        public static readonly Argument<(string key, string value)[]> InlineRunSettings = new("inline-run-settings", parse: ParseRunSettings)
        {
            Arity = ArgumentArity.ZeroOrMore
        };

        public static readonly Option<string> SettingsOption = new ForwardedOption<string>(new string[] { "-s", "--settings" }, LocalizableStrings.CmdSettingsDescription)
        {
            ArgumentHelpName = LocalizableStrings.CmdSettingsFile
        }.ForwardAsSingle(o => $"-property:VSTestSetting={SurroundWithDoubleQuotes(CommandDirectoryContext.GetFullPath(o))}");

        public static readonly Option<bool> ListTestsOption = new ForwardedOption<bool>(new string[] { "-t", "--list-tests" }, LocalizableStrings.CmdListTestsDescription)
              .ForwardAs("-property:VSTestListTests=true");

        public static readonly Option<IEnumerable<string>> EnvOption = new Option<IEnumerable<string>>(new string[] { "-e", "--environment" }, LocalizableStrings.CmdEnvironmentVariableDescription)
        {
            ArgumentHelpName = LocalizableStrings.CmdEnvironmentVariableExpression
        }.AllowSingleArgPerToken();

        public static readonly Option<string> FilterOption = new ForwardedOption<string>("--filter", LocalizableStrings.CmdTestCaseFilterDescription)
        {
            ArgumentHelpName = LocalizableStrings.CmdTestCaseFilterExpression
        }.ForwardAsSingle(o => $"-property:VSTestTestCaseFilter={SurroundWithDoubleQuotes(o)}");

        public static readonly Option<IEnumerable<string>> AdapterOption = new ForwardedOption<IEnumerable<string>>(new string[] { "--test-adapter-path" }, LocalizableStrings.CmdTestAdapterPathDescription)
        {
            ArgumentHelpName = LocalizableStrings.CmdTestAdapterPath
        }.ForwardAsSingle(o => $"-property:VSTestTestAdapterPath={SurroundWithDoubleQuotes(string.Join(";", o.Select(CommandDirectoryContext.GetFullPath)))}")
        .AllowSingleArgPerToken();

        public static readonly Option<IEnumerable<string>> LoggerOption = new ForwardedOption<IEnumerable<string>>(new string[] { "-l", "--logger" }, LocalizableStrings.CmdLoggerDescription)
        {
            ArgumentHelpName = LocalizableStrings.CmdLoggerOption
        }.ForwardAsSingle(o =>
        {
            var loggersString = string.Join(";", GetSemiColonEscapedArgs(o));

            return $"-property:VSTestLogger={SurroundWithDoubleQuotes(loggersString)}";
        })
        .AllowSingleArgPerToken();

        public static readonly Option<string> OutputOption = new ForwardedOption<string>(new string[] { "-o", "--output" }, LocalizableStrings.CmdOutputDescription)
        {
            ArgumentHelpName = LocalizableStrings.CmdOutputDir
        }.ForwardAsSingle(o => $"-property:OutputPath={SurroundWithDoubleQuotes(CommandDirectoryContext.GetFullPath(o))}");

        public static readonly Option<string> DiagOption = new ForwardedOption<string>(new string[] { "-d", "--diag" }, LocalizableStrings.CmdPathTologFileDescription)
        {
            ArgumentHelpName = LocalizableStrings.CmdPathToLogFile
        }
        .ForwardAsSingle(o => $"-property:VSTestDiag={SurroundWithDoubleQuotes(CommandDirectoryContext.GetFullPath(o))}");

        public static readonly Option<bool> NoBuildOption = new ForwardedOption<bool>("--no-build", LocalizableStrings.CmdNoBuildDescription)
            .ForwardAs("-property:VSTestNoBuild=true");

        public static readonly Option<string> ResultsOption = new ForwardedOption<string>(new string[] { "--results-directory" }, LocalizableStrings.CmdResultsDirectoryDescription)
        {
            ArgumentHelpName = LocalizableStrings.CmdPathToResultsDirectory
        }.ForwardAsSingle(o => $"-property:VSTestResultsDirectory={SurroundWithDoubleQuotes(CommandDirectoryContext.GetFullPath(o))}");

        public static readonly Option<IEnumerable<string>> CollectOption = new ForwardedOption<IEnumerable<string>>("--collect", LocalizableStrings.cmdCollectDescription)
        {
            ArgumentHelpName = LocalizableStrings.cmdCollectFriendlyName
        }.ForwardAsSingle(o => $"-property:VSTestCollect=\"{string.Join(";", GetSemiColonEscapedArgs(o))}\"")
        .AllowSingleArgPerToken();

        public static readonly Option<bool> BlameOption = new ForwardedOption<bool>("--blame", LocalizableStrings.CmdBlameDescription)
            .ForwardAs("-property:VSTestBlame=true");

        public static readonly Option<bool> BlameCrashOption = new ForwardedOption<bool>("--blame-crash", LocalizableStrings.CmdBlameCrashDescription)
            .ForwardAs("-property:VSTestBlameCrash=true");

        public static readonly Argument<string> BlameCrashDumpArgument = new Argument<string>(LocalizableStrings.CrashDumpTypeArgumentName).FromAmong(new string[] { "full", "mini" });

        public static readonly Option<string> BlameCrashDumpOption = new ForwardedOption<string>("--blame-crash-dump-type", LocalizableStrings.CmdBlameCrashDumpTypeDescription)
            .ForwardAsMany(o => new[] { "-property:VSTestBlameCrash=true", $"-property:VSTestBlameCrashDumpType={o}" });

        public static readonly Option<string> BlameCrashAlwaysOption = new ForwardedOption<string>("--blame-crash-collect-always", LocalizableStrings.CmdBlameCrashCollectAlwaysDescription)
            .ForwardAsMany(o => new[] { "-property:VSTestBlameCrash=true", "-property:VSTestBlameCrashCollectAlways=true" });

        public static readonly Option<bool> BlameHangOption = new ForwardedOption<bool>("--blame-hang", LocalizableStrings.CmdBlameHangDescription)
            .ForwardAs("-property:VSTestBlameHang=true");

        public static readonly Argument<string> BlameHangDumpArgument = new Argument<string>(LocalizableStrings.HangDumpTypeArgumentName).FromAmong(new string[] { "full", "mini", "none" });

        public static readonly Option<string> BlameHangDumpOption = new ForwardedOption<string>("--blame-hang-dump-type", LocalizableStrings.CmdBlameHangDumpTypeDescription)
            .ForwardAsMany(o => new[] { "-property:VSTestBlameHang=true", $"-property:VSTestBlameHangDumpType={o}" });

        public static readonly Option<string> BlameHangTimeoutOption = new ForwardedOption<string>("--blame-hang-timeout", LocalizableStrings.CmdBlameHangTimeoutDescription)
        {
            ArgumentHelpName = LocalizableStrings.HangTimeoutArgumentName
        }.ForwardAsMany(o => new[] { "-property:VSTestBlameHang=true", $"-property:VSTestBlameHangTimeout={o}" });

        public static readonly Option<bool> NoLogoOption = new ForwardedOption<bool>("--nologo", LocalizableStrings.CmdNoLogo)
            .ForwardAs("-property:VSTestNoLogo=nologo");

        public static readonly Option<bool> NoRestoreOption = CommonOptions.NoRestoreOption;

        public static readonly Option<string> FrameworkOption = CommonOptions.FrameworkOption(LocalizableStrings.FrameworkOptionDescription);

        public static readonly Option ConfigurationOption = CommonOptions.ConfigurationOption(LocalizableStrings.ConfigurationOptionDescription);

        private static readonly Command Command = ConstructCommand();

        public static Command GetCommand()
        {
            return Command;
        }

        private static Command ConstructCommand()
        {
            var command = new DocumentedCommand("test", DocsLink, LocalizableStrings.AppFullName);
            command.TreatUnmatchedTokensAsErrors = false;
            command.AddArgument(SlnOrProjectArgument);

            command.AddOption(SettingsOption);
            command.AddOption(ListTestsOption);
            command.AddOption(EnvOption);
            command.AddOption(FilterOption);
            command.AddOption(AdapterOption);
            command.AddOption(LoggerOption);
            command.AddOption(OutputOption);
            command.AddOption(DiagOption);
            command.AddOption(NoBuildOption);
            command.AddOption(ResultsOption);
            command.AddOption(CollectOption);
            command.AddOption(BlameOption);
            command.AddOption(BlameCrashOption);
            command.AddOption(BlameCrashDumpOption);
            command.AddOption(BlameCrashAlwaysOption);
            command.AddOption(BlameHangOption);
            command.AddOption(BlameHangDumpOption);
            command.AddOption(BlameHangTimeoutOption);
            command.AddOption(NoLogoOption);
            command.AddOption(ConfigurationOption);
            command.AddOption(FrameworkOption);
            command.AddOption(CommonOptions.RuntimeOption.WithHelpDescription(command, LocalizableStrings.RuntimeOptionDescription));
            command.AddOption(NoRestoreOption);
            command.AddOption(CommonOptions.InteractiveMsBuildForwardOption);
            command.AddOption(CommonOptions.VerbosityOption);
            command.AddOption(CommonOptions.ArchitectureOption);
            command.AddOption(CommonOptions.OperatingSystemOption);
            command.AddArgument(ForwardedArgs);
            command.AddArgument(InlineRunSettings);

            command.SetHandler(TestCommand.Run);

            return command;
        }

        private static string GetSemiColonEscapedstring(string arg)
        {
            if (arg.IndexOf(";") != -1)
            {
                return arg.Replace(";", "%3b");
            }

            return arg;
        }

        private static string[] GetSemiColonEscapedArgs(IEnumerable<string> args)
        {
            int counter = 0;
            string[] array = new string[args.Count()];

            foreach (string arg in args)
            {
                array[counter++] = GetSemiColonEscapedstring(arg);
            }

            return array;
        }

        /// <summary>
        /// Adding double quotes around the property helps MSBuild arguments parser and avoid incorrect splits on ',' or ';'.
        /// </summary>
        internal /* for testing purposes */ static string SurroundWithDoubleQuotes(string input)
        {
            if (input is null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            // If already escaped by double quotes then return original string.
            if (input.StartsWith("\"", StringComparison.Ordinal)
                && input.EndsWith("\"", StringComparison.Ordinal))
            {
                return input;
            }

            // We want to count the number of trailing backslashes to ensure
            // we will have an even number before adding the final double quote.
            // Otherwise the last \" will be interpreted as escaping the double
            // quote rather than a backslash and a double quote.
            var trailingBackslashesCount = 0;
            for (int i = input.Length - 1; i >= 0; i--)
            {
                if (input[i] == '\\')
                {
                    trailingBackslashesCount++;
                }
                else
                {
                    break;
                }
            }

            return trailingBackslashesCount % 2 == 0
                ? string.Concat("\"", input, "\"")
                : string.Concat("\"", input, "\\\"");
        }
    }
}
