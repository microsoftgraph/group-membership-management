// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.DestinationAttributesUpdater
{
    public static partial class LogMessages
    {
        // ── Generic Function Lifecycle ──

        [LoggerMessage(EventId = 100000, Level = LogLevel.Debug,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 100001, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 100002, Level = LogLevel.Error,
            Message = "{FunctionName} function failed")]
        public static partial void FunctionFailed(this ILogger logger, string functionName);

        // ── OrchestratorFunction ──

        [LoggerMessage(EventId = 100010, Level = LogLevel.Error,
            Message = "An unexpected error occurred in OrchestratorFunction")]
        public static partial void OrchestratorUnexpectedException(this ILogger logger, Exception exception);

        // ── DestinationReaderFunction ──

        [LoggerMessage(EventId = 100020, Level = LogLevel.Information,
            Message = "DestinationReaderFunction retrieved {DestinationCount} destinations for type {DestinationType}")]
        public static partial void DestinationsRetrieved(this ILogger logger, int destinationCount, string destinationType);

        // ── AttributeReaderFunction ──

        [LoggerMessage(EventId = 100030, Level = LogLevel.Information,
            Message = "AttributeReaderFunction retrieved {AttributeCount} destination attributes for type {DestinationType}")]
        public static partial void AttributesRetrieved(this ILogger logger, int attributeCount, string destinationType);

        // ── AttributeCacheUpdaterFunction ──

        [LoggerMessage(EventId = 100040, Level = LogLevel.Information,
            Message = "AttributeCacheUpdaterFunction: jobId {JobId}")]
        public static partial void AttributesUpdated(this ILogger logger, Guid jobId);
    }
}
