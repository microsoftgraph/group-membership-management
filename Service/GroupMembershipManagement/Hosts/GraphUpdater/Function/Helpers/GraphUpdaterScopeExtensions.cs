// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Entities;
using System;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public static class GraphUpdaterScopeExtensions
    {
        public static IDisposable? BeginGraphUpdaterScope(this ILogger logger, GraphUpdaterRequestBase request)
        {
            if (request?.SyncJob == null)
                return null;

            Dictionary<string, object> additionalProperties = null;

            if (request.MessageIndex.HasValue)
            {
                additionalProperties = new Dictionary<string, object>
                {
                    ["MessageIndex"] = request.MessageIndex.Value,
                    ["TotalMessageCount"] = request.TotalMessageCount ?? 0,
                    ["Instance"] = request.Instance ?? string.Empty
                };
            }

            return logger.BeginSyncJobScope(request.SyncJob, additionalProperties);
        }
    }
}
