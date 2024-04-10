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
    public class LogsRuntimeRetrievalService : IRuntimeRetrievalService
    {
        private readonly IJobSchedulerConfig _jobSchedulerConfig;
        private readonly LogsQueryClient _logsQueryClient;

        public LogsRuntimeRetrievalService(
            IJobSchedulerConfig jobSchedulerConfig,
            LogsQueryClient logsQueryClient
            )
        {
            _jobSchedulerConfig = jobSchedulerConfig;
            _logsQueryClient = logsQueryClient;
        }

        public async Task<Dictionary<string, double>> GetRunTimesInSecondsAsync()
        {
            var runtimes = new Dictionary<string, double>();

            if (_jobSchedulerConfig.GetRunTimeFromLogs)
            {
                var metricToUse = _jobSchedulerConfig.RunTimeMetric ?? "MedianProcessingTime";

                var queryResults = await _logsQueryClient.QueryWorkspaceAsync(
                                                    _jobSchedulerConfig.WorkspaceId,
                                                    _jobSchedulerConfig.RunTimeQuery,
                                                    new QueryTimeRange(TimeSpan.FromDays(_jobSchedulerConfig.RunTimeRangeInDays)));

                if (queryResults.Value.Status == Azure.Monitor.Query.Models.LogsQueryResultStatus.Success)
                {
                    foreach (var row in queryResults.Value.Table.Rows)
                    {
                        var destinationString = row.GetString("Destination");
                        var groupRuntime = row.GetDouble(metricToUse);
                        var runtime = !groupRuntime.HasValue || groupRuntime <= 0
                                        ? _jobSchedulerConfig.DefaultRuntimeSeconds
                                        : groupRuntime.Value;

                        if (destinationString == "N/A" || destinationString == "")
                            continue;

                        runtimes.Add(destinationString, runtime);
                    }
                }
            }

            runtimes.Add("Default", _jobSchedulerConfig.DefaultRuntimeSeconds);

            return runtimes;
        }
    }
}
