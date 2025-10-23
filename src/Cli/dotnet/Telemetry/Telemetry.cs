// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Frozen;
using System.Diagnostics;
using Microsoft.DotNet.Cli.Utils;

namespace Microsoft.DotNet.Cli.Telemetry;

public class Telemetry : ITelemetry
{
    internal static string? CurrentSessionId = null;
    internal static bool DisabledForTests = false;
    private static FrozenDictionary<string, object?> s_commonProperties = null!;
    private Task? _trackEventTask;

    private const string ConnectionString = "InstrumentationKey=74cc1c9e-3e6e-4d05-b3fc-dde9101d0254";

    public bool Enabled { get; }

    public Telemetry() : this(null) { }

    public Telemetry(
        string? sessionId,
        IEnvironmentProvider? environmentProvider = null)
    {

        if (DisabledForTests)
        {
            return;
        }

        environmentProvider ??= new EnvironmentProvider();

        Enabled = !environmentProvider.GetEnvironmentVariableAsBool(EnvironmentVariableNames.TELEMETRY_OPTOUT, defaultValue: CompileOptions.TelemetryOptOutDefault);

        if (!Enabled)
        {
            return;
        }

        // Store the session ID in a static field so that it can be reused
        if (!string.IsNullOrEmpty(sessionId))
        {
            CurrentSessionId = sessionId;
        }
        else if (CurrentSessionId == null)
        {
            // Generate a new session ID if not provided
            CurrentSessionId = Guid.NewGuid().ToString();
        }

        s_commonProperties = new TelemetryCommonProperties().GetTelemetryCommonProperties(CurrentSessionId);
    }

    internal static void DisableForTests()
    {
        DisabledForTests = true;
        CurrentSessionId = null;
    }

    internal static void EnableForTests()
    {
        DisabledForTests = false;
    }

    public void TrackEvent(string? eventName, IDictionary<string, string?>? properties,
        IDictionary<string, double>? measurements)
    {
        if (!Enabled)
        {
            return;
        }
        if (eventName is null)
        {
            return;
        }

        //continue the task in different threads
        if (_trackEventTask == null)
        {
            _trackEventTask = Task.Run(() => TrackEventTask(eventName, properties, measurements));
        }
        else
        {
            _trackEventTask = _trackEventTask.ContinueWith(_ => TrackEventTask(eventName, properties, measurements));
        }
    }

    public void Flush()
    {
        if (!Enabled || _trackEventTask == null)
        {
            return;
        }

        _trackEventTask.Wait();
    }

    public void ThreadBlockingTrackEvent(string? eventName, IDictionary<string, string?>? properties, IDictionary<string, double>? measurements)
    {
        if (!Enabled)
        {
            return;
        }
        if (eventName is null)
        {
            return;
        }
        TrackEventTask(eventName, properties, measurements);
    }

    private static void TrackEventTask(
        string eventName,
        IDictionary<string, string?>? properties,
        IDictionary<string, double>? measurements)
    {
        try
        {
            Activity.Current?.AddEvent(CreateActivityEvent(PrependProducerNamespace(eventName), properties, measurements));
        }
        catch (Exception e)
        {
            Debug.Fail(e.ToString());
        }
    }

    private static string PrependProducerNamespace(string eventName) => $"dotnet/cli/{eventName}";

    private static ActivityEvent CreateActivityEvent(
        string eventName,
        IDictionary<string, string?>? properties,
        IDictionary<string, double>? measurements)
    {
        var tags = MakeTags(properties, measurements);
        return new ActivityEvent(eventName, tags: tags);
    }

    private static ActivityTagsCollection MakeTags(IDictionary<string, string?>? eventProperties, IDictionary<string, double>? eventMeasurements)
    {
        var tags = new ActivityTagsCollection
        (
            s_commonProperties
        );
        if (CurrentSessionId is not null)
        {
            tags.Add("sessionId", CurrentSessionId);
        }

        if (eventProperties is not null)
        {
            foreach (var property in eventProperties)
            {
                if (property.Value is null)
                {
                    continue; // Skip null properties
                }
                tags.TryAdd(property.Key, property.Value);
            }
        }
        if (eventMeasurements is not null)
        {
            foreach (var measurement in eventMeasurements)
            {
                tags.TryAdd(measurement.Key, measurement.Value);
            }
        }
        return tags;
    }
}
