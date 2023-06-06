﻿// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Extensions.Logging;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Microsoft.NET.Build.Containers.Logging;

/// <summary>
/// Implements an ILogger that passes the logs to the wrapped TaskLoggingHelper.
/// </summary>
internal sealed class MSBuildLogger : ILogger
{
    private static readonly IDisposable Scope = new DummyDisposable();

    private readonly string _categoryHeader;
    private readonly TaskLoggingHelper _loggingHelper;

    public MSBuildLogger(string category, TaskLoggingHelper loggingHelperToWrap)
    {
        _categoryHeader = category + ": ";
        _loggingHelper = loggingHelperToWrap;
    }

    public IDisposable? BeginScope<TState>(TState? state) where TState : notnull => Scope;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        switch (logLevel)
        {
            case LogLevel.Trace:
                _loggingHelper.LogMessage(MessageImportance.Low, _categoryHeader + formatter(state, exception));
                break;
            case LogLevel.Debug:
            case LogLevel.Information:
                _loggingHelper.LogMessage(MessageImportance.High, _categoryHeader + formatter(state, exception));
                break;
            case LogLevel.Warning:
                _loggingHelper.LogWarning(_categoryHeader + formatter(state, exception));
                break;
            case LogLevel.Error:
            case LogLevel.Critical:
                _loggingHelper.LogError(_categoryHeader + formatter(state, exception));
                break;
            case LogLevel.None:
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// A simple disposable to describe scopes with <see cref="BeginScope{TState}(TState)"/>.
    /// </summary>
    private sealed class DummyDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
