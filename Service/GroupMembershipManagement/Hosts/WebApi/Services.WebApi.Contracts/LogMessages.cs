// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;

namespace Hosts.WebApi
{
    public static partial class LogMessages
    {
        // ── Generic operation lifecycle (90000-90099) ──

        [LoggerMessage(EventId = 90000, Level = LogLevel.Information,
            Message = "{OperationName} started")]
        public static partial void OperationStarted(this ILogger logger, string operationName);

        [LoggerMessage(EventId = 90001, Level = LogLevel.Information,
            Message = "{OperationName} completed")]
        public static partial void OperationCompleted(this ILogger logger, string operationName);

        [LoggerMessage(EventId = 90002, Level = LogLevel.Error,
            Message = "{OperationName} failed")]
        public static partial void OperationFailed(this ILogger logger, string operationName, Exception exception);

        // ── RequestHandlerBase lifecycle (90010-90019) ──
        // Used by Services.Contracts.RequestHandlerBase<TReq,TRes> for every request.

        [LoggerMessage(EventId = 90010, Level = LogLevel.Information,
            Message = "Started execution of request {RequestType} ({InstanceId})")]
        public static partial void RequestStarted(this ILogger logger, string requestType, Guid instanceId);

        [LoggerMessage(EventId = 90011, Level = LogLevel.Information,
            Message = "Completed execution of request {RequestType} ({InstanceId})")]
        public static partial void RequestCompleted(this ILogger logger, string requestType, Guid instanceId);

        // ── GetSupportEmailHandler (91300-91349) ──

        [LoggerMessage(EventId = 91300, Level = LogLevel.Information,
            Message = "Retrieved secret '{SecretName}' from Key Vault")]
        public static partial void SecretRetrievedFromKeyVault(this ILogger logger, string secretName);

        [LoggerMessage(EventId = 91301, Level = LogLevel.Warning,
            Message = "Failed to retrieve secret '{SecretName}' from Key Vault")]
        public static partial void SecretRetrievalFailedFromKeyVault(this ILogger logger, string secretName, Exception exception);

        [LoggerMessage(EventId = 91302, Level = LogLevel.Error,
            Message = "Unexpected error while retrieving support email addresses")]
        public static partial void SupportEmailRetrievalFailed(this ILogger logger, Exception exception);
    }
}
