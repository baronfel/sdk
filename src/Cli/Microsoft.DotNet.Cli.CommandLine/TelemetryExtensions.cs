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
        public SymbolTelemetryReporter? TelemetryReporter { get; set; }
    }

    private static readonly Func<string, SymbolTelemetryReporter> s_boolTelemetryReporter = name => TransformValue<bool>(value =>
        [
            new (name, value ? "true" : "false")
        ]);

    private static string TrimOptionPrefix(string optionName) =>
        optionName.TrimStart(['-', '/']);

    private static readonly Dictionary<Symbol, TelemetryConfiguration> s_telemetryConfigs = new();
    private static readonly Lock s_lock = new();

    public delegate KeyValuePair<string, string?>[]? SymbolTelemetryReporter<T>(T value);
    public delegate KeyValuePair<string, string?>[]? SymbolTelemetryReporter(object? value);

    /// <summary>
    /// A wrapper to transform a telemetry reporter function that works on a specific type T
    /// into one that works on object?, which is the general interface used by our telemetry reporting.
    /// </summary>
    public static SymbolTelemetryReporter TransformValue<T>(SymbolTelemetryReporter<T> telemetryReporter) =>
        (obj) => obj is T value ? telemetryReporter(value) : null;

    extension (Symbol symbol)
    {
        public bool SendInTelemetry =>
            s_telemetryConfigs.TryGetValue(symbol, out var config) && config.ShouldSend;

        /// <summary>
        /// If an option is allowed to be reported to telemetry, then this method will return the reportable values.
        /// </summary>
        public KeyValuePair<string, string?>[]? ReportableValues(object? value) =>
            s_telemetryConfigs.TryGetValue(symbol, out var config) && config.TelemetryReporter != null
                ? config.TelemetryReporter(value)
                : null;
    }

    extension<T>(Option<T> option)
    {
        public Option<T> ReportInTelemetry(SymbolTelemetryReporter<T> reporter)
        {
            lock (s_lock)
            {
                if (s_telemetryConfigs.TryGetValue(option, out var config))
                {
                    config.ShouldSend = true;
                    config.TelemetryReporter = TransformValue<T>(reporter);
                    s_telemetryConfigs[option] = config;
                }
                else
                {
                    s_telemetryConfigs[option] = new TelemetryConfiguration
                    {
                        ShouldSend = true,
                        TelemetryReporter = TransformValue<T>(reporter)
                    };
                }
            }
            return option;
        }

        public Option<T> ReportInTelemetry(Func<T, string?> reporter)
        {
            lock (s_lock)
            {
                if (s_telemetryConfigs.TryGetValue(option, out var config))
                {
                    config.ShouldSend = true;
                    config.TelemetryReporter = TransformValue<T>(v => {
                        var reportValue = reporter(v);
                        return reportValue is not null
                            ? [new (TrimOptionPrefix(option.Name), reportValue)]
                            : null;
                    });
                    s_telemetryConfigs[option] = config;
                }
                else
                {
                    s_telemetryConfigs[option] = new TelemetryConfiguration
                    {
                        ShouldSend = true,
                        TelemetryReporter = TransformValue<T>(v => {
                            var reportValue = reporter(v);
                            return reportValue is not null
                                ? [new (TrimOptionPrefix(option.Name), reportValue)]
                                : null;
                        })
                    };
                }
            }
            return option;
        }
    }

    extension<T>(Argument<T> argument)
    {
        public Argument<T> ReportInTelemetry(SymbolTelemetryReporter<T> reporter)
        {
            lock (s_lock)
            {
                if (s_telemetryConfigs.TryGetValue(argument, out var config))
                {
                    config.ShouldSend = true;
                    config.TelemetryReporter = TransformValue<T>(reporter);
                    s_telemetryConfigs[argument] = config;
                }
                else
                {
                    s_telemetryConfigs[argument] = new TelemetryConfiguration
                    {
                        ShouldSend = true,
                        TelemetryReporter = TransformValue<T>(reporter)
                    };
                }
            }
            return argument;
        }

        public Argument<T> ReportInTelemetry(Func<T, string?> reporter)
        {
            lock (s_lock)
            {
                if (s_telemetryConfigs.TryGetValue(argument, out var config))
                {
                    config.ShouldSend = true;
                    config.TelemetryReporter = TransformValue<T>(v => {
                        var reportValue = reporter(v);
                        return reportValue is not null
                            ? [new (argument.Name, reportValue)]
                            : null;
                    });
                    s_telemetryConfigs[argument] = config;
                }
                else
                {
                    s_telemetryConfigs[argument] = new TelemetryConfiguration
                    {
                        ShouldSend = true,
                        TelemetryReporter = TransformValue<T>(v => {
                            var reportValue = reporter(v);
                            return reportValue is not null
                                ? [new (argument.Name, reportValue)]
                                : null;
                        })
                    };
                }
            }
            return argument;
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
                    config.TelemetryReporter = s_boolTelemetryReporter(TrimOptionPrefix(option.Name));
                    s_telemetryConfigs[option] = config;
                }
                else
                {
                    s_telemetryConfigs[option] = new TelemetryConfiguration
                    {
                        ShouldSend = true,
                        TelemetryReporter = s_boolTelemetryReporter(TrimOptionPrefix(option.Name))
                    };
                }
            }
            return option;
        }
    }

    private static SymbolTelemetryReporter<T> EnumTelemetryReporter<T>(string optionName) where T : struct, System.Enum =>
        (value) => {
            var reportValue = Enum.GetName<T>(value);
            return reportValue is not null
                ? [new (optionName, reportValue)]
                : null;
        };

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
                    config.TelemetryReporter = TransformValue<T>(EnumTelemetryReporter<T>(TrimOptionPrefix(option.Name)));
                    s_telemetryConfigs[option] = config;
                }
                else
                {
                    s_telemetryConfigs[option] = new TelemetryConfiguration
                    {
                        ShouldSend = true,
                        TelemetryReporter = TransformValue<T>(EnumTelemetryReporter<T>(TrimOptionPrefix(option.Name)))
                    };
                }
            }
            return option;
        }
    }
}
