// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.SqlDataChecker
{
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 240000, Level = LogLevel.Debug,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 240001, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        // ── TableNameReader ──

        [LoggerMessage(EventId = 240010, Level = LogLevel.Information,
            Message = "{TableName} exists")]
        public static partial void TableExists(this ILogger logger, string tableName);

        [LoggerMessage(EventId = 240011, Level = LogLevel.Information,
            Message = "{TableName} does not exist")]
        public static partial void TableDoesNotExist(this ILogger logger, string tableName);

        [LoggerMessage(EventId = 240012, Level = LogLevel.Information,
            Message = "Missing SqlDataChecker pipeline run(s)")]
        public static partial void MissingPipelineRuns(this ILogger logger);

        // ── ThresholdReader ──

        [LoggerMessage(EventId = 240020, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed. Found {ThresholdCount} column-specific threshold(s).")]
        public static partial void ThresholdReaderCompleted(this ILogger logger, string functionName, int thresholdCount);

        [LoggerMessage(EventId = 240021, Level = LogLevel.Error,
            Message = "{FunctionName} failed to read column thresholds. Failing pipeline to avoid running with incomplete configuration.")]
        public static partial void ThresholdReaderFailed(this ILogger logger, string functionName, Exception exception);

        [LoggerMessage(EventId = 240022, Level = LogLevel.Warning,
            Message = "NullThreshold for '{AttributeName}' was out of range ({OriginalValue}), clamped to {ClampedValue}.")]
        public static partial void ThresholdClamped(this ILogger logger, string attributeName, double originalValue, double clampedValue);

        // ── DifferenceChecker ──

        [LoggerMessage(EventId = 240030, Level = LogLevel.Information,
            Message = "Column: {ColumnName} | Current NULLs: {CurrentNulls}/{CurrentRows} ({CurrentNullPct}%) | Previous NULLs: {PreviousNulls}/{PreviousRows} ({PreviousNullPct}%) | Diff: {Difference} ({PctDifference}%)")]
        public static partial void ColumnNullComparison(this ILogger logger, string columnName, int currentNulls, int currentRows, double currentNullPct, int previousNulls, int previousRows, double previousNullPct, string difference, string pctDifference);

        [LoggerMessage(EventId = 240031, Level = LogLevel.Information,
            Message = "Latest table has no data or columns — skipping null threshold check.")]
        public static partial void SkippingNullThresholdCheck(this ILogger logger);

        [LoggerMessage(EventId = 240032, Level = LogLevel.Information,
            Message = "SqlDataChecker FAILED: {ColumnCount} column(s) exceeded their null threshold: {ColumnsExceeded}")]
        public static partial void NullThresholdExceeded(this ILogger logger, int columnCount, string columnsExceeded);
    }
}
