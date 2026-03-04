// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Dotnet.Installation.Internal;
using Microsoft.DotNet.Tools.Bootstrapper.Telemetry;

namespace Microsoft.DotNet.Tools.Bootstrapper;

internal class DotnetupProgram
{
    public static int Main(string[] args)
    {
        // Handle --debug flag using the standard .NET SDK pattern
        // This is DEBUG-only and removes the --debug flag from args
        DotnetupDebugHelper.HandleDebugSwitch(ref args);

        // Set up callback to notify user when waiting for another dotnetup process
        ScopedMutex.OnWaitingForMutex = () =>
        {
            Console.WriteLine("Another dotnetup process is running. Waiting for it to finish...");
        };

        // Show first-run telemetry notice if needed
        FirstRunNotice.ShowIfFirstRun(DotnetupTelemetry.Instance.Enabled);

        // Start root activity for the entire process - if no one is configured this will no-op
        using var rootActivity = DotnetupTelemetry.CommandSource.StartActivity("dotnetup", ActivityKind.Internal);
        ApplyProcessLevelTags(rootActivity);

        try
        {
            var result = Parser.Invoke(args);
            rootActivity?.SetTag(TelemetryTagNames.Process.ExitCode, result);
            rootActivity?.SetStatus(result == 0 ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
            return result;
        }
        catch (Exception ex)
        {
            // Catch-all for unhandled exceptions
            var tags = DotnetupTelemetry.Instance.RecordException(rootActivity, ex);
            rootActivity?.AddException(ex, tags: new TagList([..tags]));
            rootActivity?.SetTag(TelemetryTagNames.Process.ExitCode, 1);

            // Log the error and return non-zero exit code
            Console.Error.WriteLine($"Error: {ex.Message}");
#if DEBUG
            Console.Error.WriteLine(ex.StackTrace);
#endif
            return 1;
        }
        finally
        {
            // Ensure telemetry is flushed before exit
            DotnetupTelemetry.Instance.Flush();
            DotnetupTelemetry.Instance.Dispose();
        }
    }

#pragma warning disable CS0649 // field is never assigned to, will always have default value (false). This is intentional - we want to be able to enable recommended tags via code changes alone when we're ready.
    private static readonly bool s_shouldIncludeRecommendedTags; // Guard rail so that we can write the code and figure out enablement later.
#pragma warning restore CS0649

    private static void ApplyProcessLevelTags(Activity? activity)
    {
        if (activity == null)
        {
            return;
        }

        // required tags for CLI semantic conventions
        activity.SetTag(TelemetryTagNames.Process.ExecutableName, "dotnetup");
        activity.SetTag(TelemetryTagNames.Process.ProcessId, Environment.ProcessId);

        // recommended tags for CLI semantic conventions -
        if (s_shouldIncludeRecommendedTags)
        {
            var args = Environment.GetCommandLineArgs();
            activity.SetTag(TelemetryTagNames.Process.CommandArgs, args);
            // we want argv[0] here, which is the fully-qualified path to the binary being run.
            activity.SetTag(TelemetryTagNames.Process.ExecutablePath, Environment.ProcessPath);
        }
    }
}
