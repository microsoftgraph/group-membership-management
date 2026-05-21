// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;

namespace Hosts.AutoApprover
{
    public static partial class LogMessages
    {
        [LoggerMessage(EventId = 250000, Level = LogLevel.Debug,
            Message = "AutoApprover is disabled. Skipping message processing.")]
        public static partial void AutoApproverDisabled(this ILogger logger);

        [LoggerMessage(EventId = 250001, Level = LogLevel.Debug,
            Message = "{FunctionName} function started")]
        public static partial void FunctionStarted(this ILogger logger, string functionName);

        [LoggerMessage(EventId = 250002, Level = LogLevel.Debug,
            Message = "AutoApprover message received. MessageId: {MessageId}. BodyLength: {BodyLength}")]
        public static partial void MessageReceived(this ILogger logger, string messageId, int bodyLength);

        [LoggerMessage(EventId = 250003, Level = LogLevel.Debug,
            Message = "{FunctionName} function completed")]
        public static partial void FunctionCompleted(this ILogger logger, string functionName);
    }
}
