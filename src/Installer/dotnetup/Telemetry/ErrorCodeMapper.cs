// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Dotnet.Installation.Internal;

namespace Microsoft.DotNet.Tools.Bootstrapper.Telemetry;

/// <summary>
/// Categorizes errors for telemetry purposes.
/// </summary>
public enum ErrorCategory
{
    /// <summary>
    /// Product errors - issues we can take action on (bugs, crashes, server issues).
    /// These count against product quality metrics.
    /// </summary>
    Product,

    /// <summary>
    /// User errors - issues caused by user input or environment we can't control.
    /// Examples: invalid version, disk full, network timeout, permission denied.
    /// These are tracked separately and don't count against success rate.
    /// </summary>
    User
}

/// <summary>
/// Error info extracted from an exception for telemetry.
/// </summary>
/// <param name="ErrorType">The error type/code for telemetry.</param>
/// <param name="Category">Whether this is a product or user error.</param>
/// <param name="StatusCode">HTTP status code if applicable.</param>
/// <param name="HResult">Win32 HResult if applicable.</param>
/// <param name="Details">Additional context (no PII - sanitized values only).</param>
public sealed record ExceptionErrorInfo(
    string ErrorType,
    ErrorCategory Category = ErrorCategory.Product,
    int? StatusCode = null,
    int? HResult = null,
    string? Details = null);

/// <summary>
/// Maps exceptions to error info for telemetry.
/// </summary>
public static class ErrorCodeMapper
{
    /// <summary>
    /// Applies error info tags to an activity. This centralizes the tag-setting logic
    /// to avoid code duplication across progress targets and telemetry classes.
    /// </summary>
    /// <param name="errorInfo">The error info to apply.</param>
    /// <param name="errorCode">Optional error code override.</param>
    public static TagList ApplyErrorTags(ExceptionErrorInfo errorInfo, string? errorCode = null)
    {
        var tagList = new TagList
        {
        };
        if (errorCode is not null)
        {
            tagList.Add("error.code", errorCode);
        }

        tagList.Add("error.category", errorInfo.Category.ToString().ToLowerInvariant());

        if (errorInfo is { HResult: { } hResult })
        {
            tagList.Add("error.hresult", hResult);
        }

        if (errorInfo is { Details: { } details })
        {
            tagList.Add("error.details", details);
        }
        return tagList;
    }

    /// <summary>
    /// Extracts error info from an exception.
    /// </summary>
    /// <param name="ex">The exception to analyze.</param>
    /// <returns>Error info with type name and contextual details.</returns>
    public static ExceptionErrorInfo GetErrorInfo(Exception ex)
    {
        // Unwrap single-inner AggregateExceptions
        if (ex is AggregateException { InnerExceptions : [var innerEx] })
        {
            return GetErrorInfo(innerEx);
        }

        // If it's a plain Exception wrapper, use the inner exception for better error type
        if (ex.GetType() == typeof(Exception) && ex.InnerException is not null)
        {
            return GetErrorInfo(ex.InnerException);
        }

        return ex switch
        {
            // DotnetInstallException has specific error codes - categorize by error code
            // Sanitize the version to prevent PII leakage (user could have typed anything)
            // For network-related errors, also check the inner exception for more details
            DotnetInstallException installEx => GetInstallExceptionErrorInfo(installEx),

            // HTTP errors: 4xx client errors are often user issues, 5xx are product/server issues
            HttpRequestException httpEx => new ExceptionErrorInfo(
                httpEx.StatusCode.HasValue ? $"Http{(int)httpEx.StatusCode}" : "HttpRequestException",
                Category: ErrorCategoryClassifier.ClassifyHttpError(httpEx.StatusCode),
                StatusCode: (int?)httpEx.StatusCode),

            // FileNotFoundException before IOException (it derives from IOException)
            // Could be user error (wrong path) or product error (our code referenced wrong file)
            // Default to product since we should handle missing files gracefully
            FileNotFoundException fnfEx => new ExceptionErrorInfo(
                "FileNotFound",
                Category: ErrorCategory.Product,
                HResult: fnfEx.HResult,
                Details: fnfEx.FileName is not null ? "file_specified" : null),

            // Permission denied - user environment issue (needs elevation or different permissions)
            UnauthorizedAccessException => new ExceptionErrorInfo(
                "PermissionDenied",
                Category: ErrorCategory.User),

            // Directory not found - could be user specified bad path
            DirectoryNotFoundException => new ExceptionErrorInfo(
                "DirectoryNotFound",
                Category: ErrorCategory.User),

            IOException ioEx => MapIOException(ioEx),

            // User cancelled the operation
            OperationCanceledException => new ExceptionErrorInfo(
                "Cancelled",
                Category: ErrorCategory.User),

            // Invalid argument - user provided bad input
            ArgumentException argEx => new ExceptionErrorInfo(
                "InvalidArgument",
                Category: ErrorCategory.User,
                Details: argEx.ParamName),

            // Invalid operation - usually a bug in our code
            InvalidOperationException => new ExceptionErrorInfo(
                "InvalidOperation",
                Category: ErrorCategory.Product),

            // Not supported - could be user trying unsupported scenario
            NotSupportedException => new ExceptionErrorInfo(
                "NotSupported",
                Category: ErrorCategory.User),

            // Timeout - network/environment issue outside our control
            TimeoutException => new ExceptionErrorInfo(
                "Timeout",
                Category: ErrorCategory.User),

            // Unknown exceptions default to product (fail-safe - we should handle known cases)
            _ => new ExceptionErrorInfo(
                ex.GetType().Name,
                Category: ErrorCategory.Product)
        };
    }

    /// <summary>
    /// Gets error info for a DotnetInstallException, enriching with inner exception details
    /// for network-related and IO-related errors.
    /// </summary>
    private static ExceptionErrorInfo GetInstallExceptionErrorInfo(
        DotnetInstallException installEx)
    {
        var errorCode = installEx.ErrorCode;
        var baseCategory = ErrorCategoryClassifier.ClassifyInstallError(errorCode);
        var details = installEx.Version is not null ? VersionSanitizer.Sanitize(installEx.Version) : null;
        int? httpStatus = null;

        if (ErrorCategoryClassifier.IsNetworkRelatedErrorCode(errorCode) && installEx.InnerException is not null)
        {
            var (refinedCategory, innerHttpStatus, innerDetails) = ErrorCategoryClassifier.AnalyzeNetworkException(installEx.InnerException);
            baseCategory = refinedCategory;
            httpStatus = innerHttpStatus;

            if (innerDetails is not null)
            {
                details = details is not null ? $"{details};{innerDetails}" : innerDetails;
            }
        }

        if (ErrorCategoryClassifier.IsIORelatedErrorCode(errorCode) && installEx.InnerException is IOException ioInner)
        {
            var (_, ioCategory, ioDetails) = ErrorCategoryClassifier.ClassifyIOErrorByHResult(ioInner.HResult);
            baseCategory = ioCategory;

            if (ioDetails is not null)
            {
                details = details is not null ? $"{details};{ioDetails}" : ioDetails;
            }
        }

        return new ExceptionErrorInfo(
            errorCode.ToString(),
            Category: baseCategory,
            StatusCode: httpStatus,
            Details: details);
    }

    private static ExceptionErrorInfo MapIOException(IOException ioEx)
    {
        // Delegate to the single-lookup classifier to avoid duplicating HResult→category logic
        var (errorType, category, details) = ErrorCategoryClassifier.ClassifyIOErrorByHResult(ioEx.HResult);

        return new ExceptionErrorInfo(
            errorType,
            Category: category,
            HResult: ioEx.HResult,
            Details: details);
    }
}
