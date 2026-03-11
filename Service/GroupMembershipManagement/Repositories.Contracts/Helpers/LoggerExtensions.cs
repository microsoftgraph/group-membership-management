// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Repositories.Contracts.Helpers
{
    public static class LoggerExtensions
    {
        public static IDisposable BeginRunIdScope(this ILogger logger, Guid? runId)
        {
            if (logger is null || !runId.HasValue)
            {
                return null;
            }

            return logger.BeginScope(new Dictionary<string, object>
            {
                ["RunId"] = runId.Value
            });
        }

        public static void LogInformationWithRunId(this ILogger logger, Guid? runId, string message)
        {
            using var scope = logger.BeginRunIdScope(runId);
            logger.LogInformation(message);
        }

        public static void LogDebugWithRunId(this ILogger logger, Guid? runId, string message)
        {
            using var scope = logger.BeginRunIdScope(runId);
            logger.LogDebug(message);
        }

        public static void LogWarningWithRunId(this ILogger logger, Guid? runId, string message, Exception ex = null)
        {
            using var scope = logger.BeginRunIdScope(runId);

            if (ex is not null)
            {
                logger.LogWarning(ex, message);
                return;
            }

            logger.LogWarning(message);
        }

        public static void LogErrorWithRunId(this ILogger logger, Guid? runId, string message, Exception ex = null)
        {
            using var scope = logger.BeginRunIdScope(runId);

            if (ex is not null)
            {
                logger.LogError(ex, message);
                return;
            }

            logger.LogError(message);
        }
    }
}
