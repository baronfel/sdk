// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using System.Reflection.Emit;

namespace Microsoft.DotNet.Cli.CommandLine;

public static class TelemetryExtensions
{
    private struct TelemetryConfiguration
    {
        /// <summary>
        /// If this Symbol should be reported in telemetry.
        /// </summary>
        public bool ShouldSend { get; set; }

        /// <summary>
        /// A function that can be used to report custom telemetry for this symbol.
        /// If not set, the value will be reported as-is.
        /// </summary>
        public Func<object?, string?>? TelemetryReporter { get; set; }
    }

    private static readonly Func<object?, string?> s_boolTelemetryReporter = b => b switch
        {
            true => "true",
            false => "false",
            _ => null
        };

    private static readonly Dictionary<Symbol, TelemetryConfiguration> s_telemetryConfigs = new();
    private static readonly Lock s_lock = new();

    public static Func<object?, string?> TransformValue<T>(Func<T?, string> telemetryReporter) =>
        (obj) => obj is T value ? telemetryReporter(value) : null;

    extension(Option option)
    {
        /// <summary>
        /// The name of the option to be reported in telemetry - this is the options' name minus any '-' or '/' leading characters
        /// </summary>
        public string ReportableName => option.Name.TrimStart(['-', '/']);

        public bool SendInTelemetry =>
            s_telemetryConfigs.TryGetValue(option, out var config) && config.ShouldSend;

        /// <summary>
        /// If an option is allowed to be reported to telemetry, then this method will return the reportable value.
        /// </summary>
        public string? ReportableValue(object? value) =>
            s_telemetryConfigs.TryGetValue(option, out var config) && config.TelemetryReporter != null
                ? config.TelemetryReporter(value)
                : null;
    }

    extension<T>(Option<T> option)
    {

        /// <summary>
        /// Marks this option to be reported in telemetry
        /// </summary>
        /// <returns></returns>
        public Option<T> ReportInTelemetry(Func<T?, string>? telemetryReporter = null)
        {
            lock (s_lock)
            {
                if (s_telemetryConfigs.TryGetValue(option, out var config))
                {
                    config.ShouldSend = true;
                    config.TelemetryReporter = telemetryReporter != null
                        ? TransformValue(telemetryReporter)
                        : null;
                    s_telemetryConfigs[option] = config;
                }
                else
                {
                    s_telemetryConfigs[option] = new TelemetryConfiguration
                    {
                        ShouldSend = true,
                        TelemetryReporter = telemetryReporter != null
                            ? TransformValue(telemetryReporter)
                            : null
                    };
                }
            }
            return option;
        }
    }

    extension (Option<bool> option)
    {
        /// <summary>
        /// Marks this option to be reported in telemetry
        /// </summary>
        /// <returns></returns>
        public Option<bool> ReportInTelemetry()
        {
            lock (s_lock)
            {
                if (s_telemetryConfigs.TryGetValue(option, out var config))
                {
                    config.ShouldSend = true;
                    config.TelemetryReporter = s_boolTelemetryReporter;
                    s_telemetryConfigs[option] = config;
                }
                else
                {
                    s_telemetryConfigs[option] = new TelemetryConfiguration
                    {
                        ShouldSend = true,
                        TelemetryReporter = s_boolTelemetryReporter
                    };
                }
            }
            return option;
        }
    }

    private static Func<object?, string?> EnumTelemetryReporter<T>() where T : struct, System.Enum =>
        static (obj) => obj is T value ? Enum.GetName<T>(value) : null;

    extension<T>(Option<T> option) where T: struct, System.Enum
    {
        /// <summary>
        /// Marks this option to be reported in telemetry
        /// </summary>
        /// <returns></returns>
        public Option<T> ReportInTelemetry()
        {
            lock (s_lock)
            {
                if (s_telemetryConfigs.TryGetValue(option, out var config))
                {
                    config.ShouldSend = true;
                    config.TelemetryReporter = EnumTelemetryReporter<T>();
                    s_telemetryConfigs[option] = config;
                }
                else
                {
                    s_telemetryConfigs[option] = new TelemetryConfiguration
                    {
                        ShouldSend = true,
                        TelemetryReporter = EnumTelemetryReporter<T>()
                    };
                }
            }
            return option;
        }
    }
}
