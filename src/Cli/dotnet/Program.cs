﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using Microsoft.DotNet.Cli.CommandFactory;
using Microsoft.DotNet.Cli.CommandFactory.CommandResolution;
using Microsoft.DotNet.Cli.Commands.Run;
using Microsoft.DotNet.Cli.Commands.Workload;
using Microsoft.DotNet.Cli.Extensions;
using Microsoft.DotNet.Cli.ShellShim;
using Microsoft.DotNet.Cli.Telemetry;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.DotNet.Cli.Utils.Extensions;
using Microsoft.DotNet.Configurer;
using Microsoft.Extensions.EnvironmentAbstractions;
using NuGet.Frameworks;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using CommandResult = System.CommandLine.Parsing.CommandResult;

namespace Microsoft.DotNet.Cli;

public class Program
{
    private static readonly string ToolPathSentinelFileName = $"{Product.Version}.toolpath.sentinel";

    public static ITelemetry TelemetryClient = null!;


    public static int Main(string[] args)
    {
        var mainTimeStamp = DateTime.Now;
        using var _flushSource = Activities.s_source;
        using var metricsProvider = Sdk.CreateMeterProviderBuilder()
            .ConfigureResource(r =>
            {
                r.AddService("dotnet-cli", serviceVersion: Product.Version);
            })
            .AddMeter(Activities.s_source.Name)
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddOtlpExporter()
            .Build();
        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .ConfigureResource(r =>
            {
                r.AddService("dotnet-cli", serviceVersion: Product.Version);
            })
            .AddSource(Activities.s_source.Name)
            .AddHttpClientInstrumentation()
            .AddOtlpExporter()
            .SetSampler(new AlwaysOnSampler())
            .Build();
        (var s_parentActivityContext, var s_activityKind) = DeriveParentActivityContextFromEnv();
        using var s_mainActivity = Activities.s_source.CreateActivity("main", s_activityKind, s_parentActivityContext);
        s_mainActivity?.Start();
        s_mainActivity?.SetStartTime(Process.GetCurrentProcess().StartTime);

        TrackHostStartup(mainTimeStamp);
        SetupMSBuildEnvironmentInvariants();
        TelemetryClient = InitializeTelemetry();
        using AutomaticEncodingRestorer _encodingRestorer = new();

        // Setting output encoding is not available on those platforms
        if (UILanguageOverride.OperatingSystemSupportsUtf8())
        {
            Console.OutputEncoding = Encoding.UTF8;
        }

        DebugHelper.HandleDebugSwitch(ref args);

        InitializeProcess();

        try
        {
            ParseResult parseResult = ParseArgs(args);
            SetDisplayName(s_mainActivity, parseResult);
            SetupDotnetFirstRun(parseResult);

            if (parseResult.CanBeInvoked())
            {
                return InvokeBuiltInCommand(parseResult);
            }
            else
            {
                try
                {
                    return LookupAndExecuteCommand(args, parseResult);
                }
                catch (CommandUnknownException e)
                {
                    Reporter.Error.WriteLine(e.Message.Red());
                    Reporter.Output.WriteLine(e.InstructionMessage);
                    return 1;
                }
            }
        }
        catch (Exception e) when (e.ShouldBeDisplayedAsError())
        {
            Reporter.Error.WriteLine(CommandLoggingContext.IsVerbose
                ? e.ToString().Red().Bold()
                : e.Message.Red().Bold());

            var commandParsingException = e as CommandParsingException;
            if (commandParsingException != null && commandParsingException.ParseResult != null)
            {
                commandParsingException.ParseResult.ShowHelp();
            }

            return 1;
        }
        catch (Exception e) when (!e.ShouldBeDisplayedAsError())
        {
            // If telemetry object has not been initialized yet. It cannot be collected
            TelemetryEventEntry.SendFiltered(e);
            Reporter.Error.WriteLine(e.ToString().Red().Bold());

            return 1;
        }
    }

    /// <summary>
    /// uses the OpenTelemetrySDK's Propagation API to derive the parent activity context and kind
    /// from the DOTNET_CLI_TRACEPARENT and DOTNET_CLI_TRACESTATE environment variables.
    /// </summary>
    /// <returns></returns>
    private static (ActivityContext parentActivityContext, ActivityKind kind) DeriveParentActivityContextFromEnv()
    {
        var traceParent = Env.GetEnvironmentVariable(Activities.DOTNET_CLI_TRACEPARENT);
        var traceState = Env.GetEnvironmentVariable(Activities.DOTNET_CLI_TRACESTATE);
        static IEnumerable<string>? GetValueFromCarrier(Dictionary<string, IEnumerable<string>?> carrier, string key)
        {
            return carrier.TryGetValue(key, out var value) ? value : null;
        }

        if (string.IsNullOrEmpty(traceParent))
        {
            return (default, ActivityKind.Internal);
        }
        var carriermap = new Dictionary<string, IEnumerable<string>?>
        {
            { "traceparent", [traceParent] },
        };
        if (!string.IsNullOrEmpty(traceState))
        {
            carriermap.Add("tracestate", [traceState]);
        }

        // Use the OpenTelemetry Propagator to extract the parent activity context and kind. For some reason this isn't set by the OTel SDK like docs say it should be.
        Sdk.SetDefaultTextMapPropagator(new CompositeTextMapPropagator([
            new TraceContextPropagator(),
            new BaggagePropagator()
        ]));
        var parentActivityContext = Propagators.DefaultTextMapPropagator.Extract(default, carriermap, GetValueFromCarrier);
        var kind = parentActivityContext.ActivityContext.IsRemote ? ActivityKind.Server : ActivityKind.Internal;

        return (parentActivityContext.ActivityContext, kind);
    }

    private static void TrackHostStartup(DateTime mainTimeStamp)
    {
        var hostStartupActivity = Activities.s_source.StartActivity("host-startup");
        hostStartupActivity?.SetStartTime(Process.GetCurrentProcess().StartTime);
        hostStartupActivity?.SetEndTime(mainTimeStamp);
        hostStartupActivity?.SetStatus(ActivityStatusCode.Ok);
        hostStartupActivity?.Dispose();
    }

    /// <summary>
    /// We have some behaviors in MSBuild that we want to enforce (either when using MSBuild API or by shelling out to it),
    /// so we set those ASAP as globally as possible.
    /// </summary>
    private static void SetupMSBuildEnvironmentInvariants()
    {
        if (string.IsNullOrEmpty(Env.GetEnvironmentVariable("MSBUILDFAILONDRIVEENUMERATINGWILDCARD")))
        {
            Environment.SetEnvironmentVariable("MSBUILDFAILONDRIVEENUMERATINGWILDCARD", "1");
        }
    }

    private static string GetCommandName(ParseResult r)
    {
        if (r.Action is Parser.PrintVersionAction)
        {
            // If the action is PrintVersionAction, we return the command name as "dotnet --version"
            return "dotnet --version";
        }
        else if (r.Action is Parser.PrintInfoAction)
        {
            // If the action is PrintHelpAction, we return the command name as "dotnet --help"
            return "dotnet --info";
        }

        // walk the parent command tree to find the top-level command name and get the full command name for this parseresult
        List<string> parentNames = [r.CommandResult.Command.Name];
        var current = r.CommandResult.Parent;
        while (current is CommandResult parentCommandResult)
        {
            parentNames.Add(parentCommandResult.Command.Name);
            current = parentCommandResult.Parent;
        }
        parentNames.Reverse();
        return string.Join(' ', parentNames);
    }
    private static void SetDisplayName(Activity? activity, ParseResult parseResult)
    {
        if (activity == null)
        {
            return;
        }
        var name = GetCommandName(parseResult);

        // Set the display name to the full command name
        activity.DisplayName = name;

        // Set the command name as an attribute for better filtering in telemetry
        activity.SetTag("command.name", name);
    }

    private static int LookupAndExecuteCommand(string[] args, ParseResult parseResult)
    {
        var _lookupExternalCommandActivity = Activities.s_source.StartActivity("lookup-external-command");
        string commandName = "dotnet-" + parseResult.GetValue(Parser.DotnetSubCommand);
        var resolvedCommandSpec = CommandResolver.TryResolveCommandSpec(
            new DefaultCommandResolverPolicy(),
            commandName,
            args.GetSubArguments(),
            FrameworkConstants.CommonFrameworks.NetStandardApp15);
        _lookupExternalCommandActivity?.Dispose();
        
        if (resolvedCommandSpec is null && TryRunFileBasedApp(parseResult) is { } fileBasedAppExitCode)
        {
            _lookupExternalCommandActivity?.Dispose();
            return fileBasedAppExitCode;
        }
        else
        {
            var resolvedCommand = CommandFactoryUsingResolver.CreateOrThrow(commandName, resolvedCommandSpec);
            _lookupExternalCommandActivity?.Dispose();
            
            using var _executionActivity = Activities.s_source.StartActivity("execute-extensible-command");
            var result = resolvedCommand.Execute();
            return result.ExitCode;
        }
    }

    static int InvokeBuiltInCommand(ParseResult parseResult)
    {
        Debug.Assert(parseResult.CanBeInvoked());
        using var _invocationActivity = Activities.s_source.StartActivity("invocation");
        try
        {
            var exitCode = parseResult.Invoke();
            return AdjustExitCode(parseResult, exitCode);
        }
        catch (Exception exception)
        {
            return Parser.ExceptionHandler(exception, parseResult);
        }
    }

    static int? TryRunFileBasedApp(ParseResult parseResult)
    {
        // If we didn't match any built-in commands, and a C# file path is the first argument,
        // parse as `dotnet run file.cs ..rest_of_args` instead.
        if (parseResult.CommandResult.Command is RootCommand
            && parseResult.GetValue(Parser.DotnetSubCommand) is { } unmatchedCommandOrFile
            && VirtualProjectBuildingCommand.IsValidEntryPointPath(unmatchedCommandOrFile))
        {
            List<string> otherTokens = new(parseResult.Tokens.Count - 1);
            foreach (var token in parseResult.Tokens)
            {
                if (token.Type != TokenType.Argument || token.Value != unmatchedCommandOrFile)
                {
                    otherTokens.Add(token.Value);
                }
            }
            parseResult = Parser.Instance.Parse(["run", unmatchedCommandOrFile, .. otherTokens]);

            return InvokeBuiltInCommand(parseResult);
        }

        return null;
    }

    private static ITelemetry InitializeTelemetry()
    {
        var telemetryClient = new Telemetry.Telemetry();
        TelemetryEventEntry.Subscribe(telemetryClient.TrackEvent);
        TelemetryEventEntry.TelemetryFilter = new TelemetryFilter(Sha256Hasher.HashWithNormalizedCasing);

        if (CommandLoggingContext.IsVerbose)
        {
            Console.WriteLine($"Telemetry is: {(telemetryClient.Enabled ? "Enabled" : "Disabled")}");
        }

        return telemetryClient;
    }

    private static ParseResult ParseArgs(string[] args)
    {
        ParseResult parseResult;
        using (var _parseActivity = Activities.s_source.StartActivity("parse"))
        {
            parseResult = Parser.Instance.Parse(args);

            // Avoid create temp directory with root permission and later prevent access in non sudo
            // This method need to be run very early before temp folder get created
            // https://github.com/dotnet/sdk/issues/20195
            SudoEnvironmentDirectoryOverride.OverrideEnvironmentVariableToTmp(parseResult);
        }


        return parseResult;
    }

    private static void SetupDotnetFirstRun(ParseResult parseResult)
    {
        using (var _firstTimeUseActivity = Activities.s_source.StartActivity("first-time-use"))
        {
            IFirstTimeUseNoticeSentinel firstTimeUseNoticeSentinel = new FirstTimeUseNoticeSentinel();

            IAspNetCertificateSentinel aspNetCertificateSentinel = new AspNetCertificateSentinel();
            IFileSentinel toolPathSentinel = new FileSentinel(
                new FilePath(
                    Path.Combine(
                        CliFolderPathCalculator.DotnetUserProfileFolderPath,
                        ToolPathSentinelFileName)));

            var environmentProvider = new EnvironmentProvider();

            bool generateAspNetCertificate = environmentProvider.GetEnvironmentVariableAsBool(EnvironmentVariableNames.DOTNET_GENERATE_ASPNET_CERTIFICATE, defaultValue: true);
            bool telemetryOptout = environmentProvider.GetEnvironmentVariableAsBool(EnvironmentVariableNames.TELEMETRY_OPTOUT, defaultValue: CompileOptions.TelemetryOptOutDefault);
            bool addGlobalToolsToPath = environmentProvider.GetEnvironmentVariableAsBool(EnvironmentVariableNames.DOTNET_ADD_GLOBAL_TOOLS_TO_PATH, defaultValue: true);
            bool nologo = environmentProvider.GetEnvironmentVariableAsBool(EnvironmentVariableNames.DOTNET_NOLOGO, defaultValue: false);
            bool skipWorkloadIntegrityCheck = environmentProvider.GetEnvironmentVariableAsBool(EnvironmentVariableNames.DOTNET_SKIP_WORKLOAD_INTEGRITY_CHECK,
                // Default the workload integrity check skip to true if the command is being ran in CI. Otherwise, false.
                defaultValue: new CIEnvironmentDetectorForTelemetry().IsCIEnvironment());

            ReportDotnetHomeUsage(environmentProvider);

            var isDotnetBeingInvokedFromNativeInstaller = false;
            if (parseResult.CommandResult.Command.Name.Equals(Parser.InstallSuccessCommand.Name))
            {
                aspNetCertificateSentinel = new NoOpAspNetCertificateSentinel();
                firstTimeUseNoticeSentinel = new NoOpFirstTimeUseNoticeSentinel();
                toolPathSentinel = new NoOpFileSentinel(exists: false);
                isDotnetBeingInvokedFromNativeInstaller = true;
            }

            var dotnetFirstRunConfiguration = new DotnetFirstRunConfiguration(
                generateAspNetCertificate: generateAspNetCertificate,
                telemetryOptout: telemetryOptout,
                addGlobalToolsToPath: addGlobalToolsToPath,
                nologo: nologo,
                skipWorkloadIntegrityCheck: skipWorkloadIntegrityCheck);

            string[] getStarOperators = ["getProperty", "getItem", "getTargetResult"];
            char[] switchIndicators = ['-', '/'];
            var getStarOptionPassed = parseResult.CommandResult.Tokens.Any(t =>
                getStarOperators.Any(o =>
                switchIndicators.Any(i => t.Value.StartsWith(i + o, StringComparison.OrdinalIgnoreCase))));

            ConfigureDotNetForFirstTimeUse(
                firstTimeUseNoticeSentinel,
                aspNetCertificateSentinel,
                toolPathSentinel,
                isDotnetBeingInvokedFromNativeInstaller,
                dotnetFirstRunConfiguration,
                environmentProvider,
                skipFirstTimeUseCheck: getStarOptionPassed);
        }
    }
    
    private static int AdjustExitCode(ParseResult parseResult, int exitCode)
    {
        if (parseResult.Errors.Count > 0)
        {
            var commandResult = parseResult.CommandResult;

            while (commandResult is not null)
            {
                if (commandResult.Command.Name == "new")
                {
                    // default parse error exit code is 1
                    // for the "new" command and its subcommands it needs to be 127
                    return 127;
                }

                commandResult = commandResult.Parent as CommandResult;
            }
        }

        return exitCode;
    }

    private static void ReportDotnetHomeUsage(IEnvironmentProvider provider)
    {
        var home = provider.GetEnvironmentVariable(CliFolderPathCalculator.DotnetHomeVariableName);
        if (string.IsNullOrEmpty(home))
        {
            return;
        }

        Reporter.Verbose.WriteLine(
            string.Format(
                LocalizableStrings.DotnetCliHomeUsed,
                home,
                CliFolderPathCalculator.DotnetHomeVariableName));
    }

    private static void ConfigureDotNetForFirstTimeUse(
       IFirstTimeUseNoticeSentinel firstTimeUseNoticeSentinel,
       IAspNetCertificateSentinel aspNetCertificateSentinel,
       IFileSentinel toolPathSentinel,
       bool isDotnetBeingInvokedFromNativeInstaller,
       DotnetFirstRunConfiguration dotnetFirstRunConfiguration,
       IEnvironmentProvider environmentProvider,
       bool skipFirstTimeUseCheck)
    {
        var isFirstTimeUse = !firstTimeUseNoticeSentinel.Exists() && !skipFirstTimeUseCheck;
        var environmentPath = EnvironmentPathFactory.CreateEnvironmentPath(isDotnetBeingInvokedFromNativeInstaller, environmentProvider);
        _ = new DotNetCommandFactory(alwaysRunOutOfProc: true);
        var aspnetCertificateGenerator = new AspNetCoreCertificateGenerator();
        var reporter = Reporter.Output;
        var dotnetConfigurer = new DotnetFirstTimeUseConfigurer(
            firstTimeUseNoticeSentinel,
            aspNetCertificateSentinel,
            aspnetCertificateGenerator,
            toolPathSentinel,
            dotnetFirstRunConfiguration,
            reporter,
            environmentPath,
            skipFirstTimeUseCheck: skipFirstTimeUseCheck);

        dotnetConfigurer.Configure();

        if (isDotnetBeingInvokedFromNativeInstaller && OperatingSystem.IsWindows())
        {
            DotDefaultPathCorrector.Correct();
        }

        if (isFirstTimeUse && !dotnetFirstRunConfiguration.SkipWorkloadIntegrityCheck)
        {
            try
            {
                WorkloadIntegrityChecker.RunFirstUseCheck(reporter);
            }
            catch (Exception)
            {
                // If the workload check fails for any reason, we want to eat the failure and continue running the command.
                reporter.WriteLine(CliStrings.WorkloadIntegrityCheckError.Yellow());
            }
        }
    }

    private static void InitializeProcess()
    {
        // by default, .NET Core doesn't have all code pages needed for Console apps.
        // see the .NET Core Notes in https://docs.microsoft.com/dotnet/api/system.diagnostics.process#-notes
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        UILanguageOverride.Setup();
    }
}
