// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using Microsoft.DotNet.Cli.CommandLine;
using Microsoft.DotNet.Cli.Commands.Build;
using Microsoft.DotNet.Cli.Commands.Clean;
using Microsoft.DotNet.Cli.Commands.Hidden.InternalReportInstallSuccess;
using Microsoft.DotNet.Cli.Commands.Pack;
using Microsoft.DotNet.Cli.Commands.Publish;
using Microsoft.DotNet.Cli.Commands.Run;
using Microsoft.DotNet.Cli.Commands.Test;
using Microsoft.DotNet.Cli.Extensions;
using Microsoft.DotNet.Cli.Utils;

namespace Microsoft.DotNet.Cli.Telemetry;

internal class TelemetryFilter(Func<string, string> hash) : ITelemetryFilter
{
    private const string ExceptionEventName = "mainCatchException/exception";
    private readonly Func<string, string> _hash = hash ?? throw new ArgumentNullException(nameof(hash));

    public IEnumerable<ApplicationInsightsEntryFormat> Filter(object objectToFilter)
    {
        var result = new List<ApplicationInsightsEntryFormat>();
        Dictionary<string, double>? measurements = null;
        string globalJsonState = string.Empty;
        if (objectToFilter is Tuple<ParseResult, Dictionary<string, double>> parseResultWithMeasurements)
        {
            objectToFilter = parseResultWithMeasurements.Item1;
            measurements = parseResultWithMeasurements.Item2;
            measurements = RemoveZeroTimes(measurements);
        }
        else if (objectToFilter is Tuple<ParseResult, Dictionary<string, double>, string> parseResultWithMeasurementsAndGlobalJsonState)
        {
            objectToFilter = parseResultWithMeasurementsAndGlobalJsonState.Item1;
            measurements = parseResultWithMeasurementsAndGlobalJsonState.Item2;
            measurements = RemoveZeroTimes(measurements);
            globalJsonState = parseResultWithMeasurementsAndGlobalJsonState.Item3;
        }

        if (objectToFilter is ParseResult parseResult)
        {
            var topLevelCommandName = parseResult.RootSubCommandResult();
            if (topLevelCommandName != null)
            {
                Dictionary<string, string?> properties = new()
                {
                    ["verb"] = topLevelCommandName
                };
                if (!string.IsNullOrEmpty(globalJsonState))
                {
                    properties["globalJson"] = globalJsonState;
                }

                result.Add(new ApplicationInsightsEntryFormat(
                    "toplevelparser/command",
                    properties,
                    measurements
                ));

                LogVerbosityForAllTopLevelCommand(result, parseResult, topLevelCommandName, measurements);
                LogVulnerableOptionForPackageUpdateCommand(result, parseResult, topLevelCommandName, measurements);

                foreach (IParseResultLogRule rule in ParseResultLogRules)
                {
                    if (rule.AllowList(parseResult, measurements) is List<ApplicationInsightsEntryFormat> ruleResult)
                    {
                        result.AddRange(ruleResult);
                    }
                }
            }
        }
        else if (objectToFilter is InstallerSuccessReport installerSuccessReport)
        {
            result.Add(new ApplicationInsightsEntryFormat(
                "install/reportsuccess",
                new Dictionary<string, string?> { { "exeName", installerSuccessReport.ExeName } }
            ));
        }
        else if (objectToFilter is Exception exception)
        {
            result.Add(new ApplicationInsightsEntryFormat(
                ExceptionEventName,
                new Dictionary<string, string?>
                {
                    {"exceptionType", exception.GetType().ToString()},
                    {"detail", ExceptionToStringWithoutMessage(exception) }
                }
            ));
        }

        return [.. result.Select(r =>
        {
            if (r.EventName == ExceptionEventName)
            {
                return r;
            }
            else
            {
                return r.WithAppliedToPropertiesValue(_hash);
            }
        })];
    }

    private static List<IParseResultLogRule> ParseResultLogRules =>
    [
        new AllowWhatTheCommandAllows(),
    ];

    private class AllowWhatTheCommandAllows() : IParseResultLogRule
    {
        public List<ApplicationInsightsEntryFormat>? AllowList(ParseResult parseResult, Dictionary<string, double>? measurements = null)
        {
            var commandResult = parseResult.CommandResult;
            if (commandResult is null)
            {
                return null;
            }

            var command = commandResult.Command;
            var commandName = command.Name;
            var commandParents = GetCommandParentNames(commandResult);
            var entry = new ApplicationInsightsEntryFormat(
                    "sublevelparser/command",
                    new Dictionary<string, string?>
                    {
                        { "verb", commandName },
                        { "topLevelVerb", commandParents is null || commandParents.Length == 0 ? null : commandParents[0]},
                        { "parents", $"[{(commandParents is null ? null : string.Join("/", commandParents))}]" }
                    },
                    measurements);

            foreach (var optionResult in commandResult.Children.OfType<OptionResult>())
            {
                var option = optionResult.Option;
                if (option is not null && option.SendInTelemetry)
                {
                    var rawValue = optionResult.GetValueOrDefault<object>();
                    var values = option.ReportableValues(rawValue);
                    if (values is null)
                    {
                        continue;
                    }
                    foreach (var (name, value) in values)
                    {
                        entry.Properties![$"option.{name}"] = value;
                    }
                }
            }

            foreach (var argumentResult in commandResult.Children.OfType<ArgumentResult>())
            {
                var argument = argumentResult.Argument;
                if (argument is not null && argument.SendInTelemetry)
                {
                    var rawValue = argumentResult.GetValueOrDefault<object>();
                    var values = argument.ReportableValues(rawValue);
                    if (values is null)
                    {
                        continue;
                    }
                    foreach (var (name, value) in values)
                    {
                        entry.Properties![$"argument.{name}"] = value;
                    }
                }
            }
            return [ entry ];
        }

        private static string[]? GetCommandParentNames(System.CommandLine.Parsing.CommandResult command)
        {
            var parents = new List<string>();
            var parent = command.Parent;
            while (parent is System.CommandLine.Parsing.CommandResult parentCommand)
            {
                parents.Add(parentCommand.Command.Name);
                parent = parentCommand.Parent;
            }
            parents.Reverse();
            return [.. parents];
        }
    }

    private static void LogVulnerableOptionForPackageUpdateCommand(
        ICollection<ApplicationInsightsEntryFormat> result,
        ParseResult parseResult,
        string topLevelCommandName,
        Dictionary<string, double>? measurements = null)
    {
        if (topLevelCommandName == "package" && parseResult.CommandResult.Command != null && parseResult.CommandResult.Command.Name == "update")
        {
            var hasVulnerableOption = parseResult.HasOption("--vulnerable");

            result.Add(new ApplicationInsightsEntryFormat(
                "sublevelparser/command",
                new Dictionary<string, string?>()
                {
                    { "verb", "package update" },
                    { "vulnerable", hasVulnerableOption.ToString()}
                },
                measurements));
        }
    }

    private static void LogVerbosityForAllTopLevelCommand(
        ICollection<ApplicationInsightsEntryFormat> result,
        ParseResult parseResult,
        string topLevelCommandName,
        Dictionary<string, double>? measurements = null)
    {
        if (parseResult.IsDotnetBuiltInCommand() &&
            parseResult.SafelyGetValueForOption<VerbosityOptions>("--verbosity") is VerbosityOptions verbosity)
        {
            result.Add(new ApplicationInsightsEntryFormat(
                "sublevelparser/command",
                new Dictionary<string, string?>()
                {
                    { "verb", topLevelCommandName},
                    { "verbosity", Enum.GetName(verbosity)}
                },
                measurements));
        }
    }

    private static string ExceptionToStringWithoutMessage(Exception e)
    {
        const string AggregateException_ToString = "{0}{1}---> (Inner Exception #{2}) {3}{4}{5}";
        if (e is AggregateException aggregate)
        {
            string text = NonAggregateExceptionToStringWithoutMessage(aggregate);

            for (int i = 0; i < aggregate.InnerExceptions.Count; i++)
            {
                text = string.Format(CultureInfo.InvariantCulture,
                                     AggregateException_ToString,
                                     text,
                                     Environment.NewLine,
                                     i,
                                     ExceptionToStringWithoutMessage(aggregate.InnerExceptions[i]),
                                     "<---",
                                     Environment.NewLine);
            }

            return text;
        }
        else
        {
            return NonAggregateExceptionToStringWithoutMessage(e);
        }
    }

    private static string NonAggregateExceptionToStringWithoutMessage(Exception e)
    {
        string s;
        const string Exception_EndOfInnerExceptionStack = "--- End of inner exception stack trace ---";


        s = e.GetType().ToString();

        if (e.InnerException != null)
        {
            s = s + " ---> " + ExceptionToStringWithoutMessage(e.InnerException) + Environment.NewLine +
            "   " + Exception_EndOfInnerExceptionStack;

        }

        var stackTrace = e.StackTrace;

        if (stackTrace != null)
        {
            s += Environment.NewLine + stackTrace;
        }

        return s;
    }

    private static Dictionary<string, double>? RemoveZeroTimes(Dictionary<string, double>? measurements)
    {
        if (measurements != null)
        {
            foreach (var measurement in measurements)
            {
                if (measurement.Value == 0)
                {
                    measurements.Remove(measurement.Key);
                }
            }
            if (measurements.Count == 0)
            {
                measurements = null;
            }
        }
        return measurements;
    }
}
