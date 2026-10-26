// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.AgentReader
{
    public static partial class LogMessages
    {
        [LoggerMessage(EventId = 260000, Level = LogLevel.Debug,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 260001, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 260010, Level = LogLevel.Information,
            Message = "Agent table population started")]
        public static partial void PopulationStarted(this ILogger logger);

        [LoggerMessage(EventId = 260011, Level = LogLevel.Information,
            Message = "Agent population {Status}. TotalEnumerated={TotalEnumerated}; Inserted={Inserted}; "
                + "FilterPassingAgents={FilterPassingAgents}; FilteredNoManager={FilteredNoManager}; "
                + "FilteredManagerEmployeeIdAbsent={FilteredManagerEmployeeIdAbsent}; "
                + "FilteredManagerEmployeeIdNonNumeric={FilteredManagerEmployeeIdNonNumeric}; "
                + "FilteredManagerEmployeeIdOutOfRange={FilteredManagerEmployeeIdOutOfRange}; "
                + "FilteredInvalidAgentObjectId={FilteredInvalidAgentObjectId}; "
                + "WrittenWithoutManager={WrittenWithoutManager}; "
                + "AccountEnabledDefaultApplied={AccountEnabledDefaultApplied}; BlueprintIdTruncated={BlueprintIdTruncated}")]
        public static partial void PopulationCounters(
            this ILogger logger, string status, long totalEnumerated, long inserted, long filterPassingAgents,
            long filteredNoManager, long filteredManagerEmployeeIdAbsent, long filteredManagerEmployeeIdNonNumeric,
            long filteredManagerEmployeeIdOutOfRange, long filteredInvalidAgentObjectId, long writtenWithoutManager,
            long accountEnabledDefaultApplied, long blueprintIdTruncated);

        [LoggerMessage(EventId = 260012, Level = LogLevel.Error,
            Message = "Agent table population failed; the orchestration must not be accepted as a successful pipeline run")]
        public static partial void PopulationFailed(this ILogger logger);

        [LoggerMessage(EventId = 260013, Level = LogLevel.Warning,
            Message = "The agents table does not allow a NULL AgentManagerId; agents without a valid manager are skipped "
                + "until the Data Factory pipeline is redeployed")]
        public static partial void AgentManagerIdNotNullable(this ILogger logger);

        [LoggerMessage(EventId = 260020, Level = LogLevel.Warning,
            Message = "AgentReader request rejected: {Reason}")]
        public static partial void InvalidRequest(this ILogger logger, string reason);

        [LoggerMessage(EventId = 260021, Level = LogLevel.Information,
            Message = "Returning existing AgentReader instance with status {RuntimeStatus}")]
        public static partial void ExistingInstance(this ILogger logger, string runtimeStatus);

        [LoggerMessage(EventId = 260022, Level = LogLevel.Information,
            Message = "AgentReader orchestration scheduled")]
        public static partial void InstanceScheduled(this ILogger logger);

        [LoggerMessage(EventId = 260023, Level = LogLevel.Information,
            Message = "AgentReader start requested through the per-run entity")]
        public static partial void StartRequested(this ILogger logger);

        [LoggerMessage(EventId = 260024, Level = LogLevel.Information,
            Message = "AgentReader start already accepted for this run; no second orchestration scheduled")]
        public static partial void DuplicateStartIgnored(this ILogger logger);

        [LoggerMessage(EventId = 260025, Level = LogLevel.Error,
            Message = "AgentReader instance was not confirmed within {TimeoutSeconds} seconds; no successful starter response returned")]
        public static partial void StartNotConfirmed(this ILogger logger, Exception exception, double timeoutSeconds);

        [LoggerMessage(EventId = 260060, Level = LogLevel.Debug,
            Message = "Agent page processed: {TotalEnumerated} enumerated, {FilterPassingAgents} eligible, {TotalFiltered} filtered, "
                + "of which {WrittenWithoutManager} were written without a manager")]
        public static partial void PageProcessed(
            this ILogger logger, long totalEnumerated, long filterPassingAgents, long totalFiltered, long writtenWithoutManager);

        [LoggerMessage(EventId = 260061, Level = LogLevel.Warning,
            Message = "Blueprint name lookup failed for {BlueprintCount} blueprints; their codes are used as descriptions")]
        public static partial void BlueprintNameLookupFailed(this ILogger logger, Exception exception, int blueprintCount);

        [LoggerMessage(EventId = 260062, Level = LogLevel.Information,
            Message = "Wrote {Written} blueprint mappings, {Resolved} with a resolved name")]
        public static partial void BlueprintMappingsWritten(this ILogger logger, int written, int resolved);
    }
}
