// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.Identity;
using Azure.Monitor.Query;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services
{
    public class DefaultRuntimeRetrievalService : IRuntimeRetrievalService
    {
        private readonly IJobSchedulerConfig _jobSchedulerConfig;
        private readonly LogsQueryClient _logsQueryClient;

        public DefaultRuntimeRetrievalService(
            IJobSchedulerConfig jobSchedulerConfig,
            LogsQueryClient logsQueryClient
            )
        {
            _jobSchedulerConfig = jobSchedulerConfig;
            _logsQueryClient = logsQueryClient;
        }

        public async Task<Dictionary<Guid, double>> GetRunTimesInSecondsAsync(List<Guid> groupIds)
        {
            var runtimes = new Dictionary<Guid, double>();

            if (_jobSchedulerConfig.GetRunTimeFromLogs)
            {
                var metricToUse = _jobSchedulerConfig.RunTimeMetric?.Equals("avg", StringComparison.InvariantCultureIgnoreCase) ?? false
                                  ? "AvgProcessingTime" : "MaxProcessingTime";

                var queryResults = await _logsQueryClient.QueryWorkspaceAsync(
                                                    _jobSchedulerConfig.WorkspaceId,
                                                    _jobSchedulerConfig.RunTimeQuery,
                                                    new QueryTimeRange(TimeSpan.FromDays(_jobSchedulerConfig.RunTimeRangeInDays)));

                if (queryResults.Value.Status == Azure.Monitor.Query.Models.LogsQueryResultStatus.Success)
                {
                    foreach (var row in queryResults.Value.Table.Rows)
                    {
                        var destinationGroupId = row.GetGuid("Destination");
                        var groupRuntime = row.GetDouble(metricToUse);
                        var runtime = !groupRuntime.HasValue || groupRuntime <= 0
                                        ? _jobSchedulerConfig.DefaultRuntimeSeconds
                                        : groupRuntime.Value;

                        if (!destinationGroupId.HasValue || destinationGroupId == Guid.Empty)
                            continue;

                        runtimes.Add(destinationGroupId.Value, runtime);
                    }
                }
            }

            runtimes.Add(Guid.Empty, _jobSchedulerConfig.DefaultRuntimeSeconds);

            return runtimes;
        }
    }
}
