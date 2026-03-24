// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Repositories.Contracts.Helpers
{
    public static class LoggerExtensions
    {
        public static IDisposable BeginRunIdScope(this ILogger logger, Guid? runId)
        {
            var resolvedRunId = CorrelationActivity.ResolveRunId(runId);
            if (!resolvedRunId.HasValue)
            {
                return null;
            }

            return logger.BeginScope(new Dictionary<string, object>
            {
                ["RunId"] = resolvedRunId.Value
            });
        }

        public static IDisposable BeginSyncJobScope(this ILogger logger, SyncJob syncJob)
        {
            return BeginSyncJobScope(logger, syncJob, null);
        }

        public static IDisposable BeginSyncJobScope(this ILogger logger, SyncJob syncJob,
            Dictionary<string, object> additionalProperties)
        {
            if (syncJob == null)
            {
                return null;
            }

            var scopeValues = syncJob.ToDictionary()
                                     .ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

            if (syncJob.RunId.HasValue)
            {
                scopeValues[CorrelationActivity.RunIdPropertyName] = syncJob.RunId.Value;
            }

            if (syncJob.Id != Guid.Empty)
            {
                scopeValues[CorrelationActivity.SyncJobIdPropertyName] = syncJob.Id;
            }

            if (additionalProperties != null)
            {
                foreach (var kvp in additionalProperties)
                {
                    scopeValues[kvp.Key] = kvp.Value;
                }
            }

            var loggerScope = logger.BeginScope(scopeValues);
            var runIdScope = CorrelationActivity.SetScopedRunId(syncJob.RunId);

            if (runIdScope == null)
                return loggerScope;

            return new CompositeScope(loggerScope, runIdScope);
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

        private sealed class CompositeScope : IDisposable
        {
            private readonly IDisposable _loggerScope;
            private readonly IDisposable _runIdScope;

            public CompositeScope(IDisposable loggerScope, IDisposable runIdScope)
            {
                _loggerScope = loggerScope;
                _runIdScope = runIdScope;
            }

            public void Dispose()
            {
                _runIdScope?.Dispose();
                _loggerScope?.Dispose();
            }
        }
    }
}
