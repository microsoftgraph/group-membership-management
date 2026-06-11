// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Data.SqlTypes;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class JobTrackerFunction
    {
        private readonly ILogger<JobTrackerFunction> _logger;

        public JobTrackerFunction(ILogger<JobTrackerFunction> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Function(nameof(JobTrackerFunction))]
        public async Task<int> TrackJobFrequencyAsync([ActivityTrigger] SyncJob syncJob)
        {
            using (_logger.BeginSyncJobScope(syncJob))
            {
                _logger.FunctionStarted(nameof(JobTrackerFunction));
                var frequency = 0;
                if (syncJob != null && syncJob.LastSuccessfulRunTime != SqlDateTime.MinValue.Value)
                {
                    if (syncJob.Status == SyncStatus.Idle.ToString())
                    {
                        var timeDifference = (int)(DateTime.UtcNow - syncJob.LastSuccessfulRunTime).TotalHours;
                        frequency = timeDifference / syncJob.Period;
                    }
                    else if (syncJob.Status == SyncStatus.InProgress.ToString() || syncJob.Status == SyncStatus.StuckInProgress.ToString())
                    {
                        var timeDifference = (int)(DateTime.UtcNow - syncJob.LastSuccessfulStartTime).TotalHours;
                        frequency = timeDifference / syncJob.Period;
                    }
                }

                _logger.FunctionCompleted(nameof(JobTrackerFunction));
                return frequency;
            }
        }
    }
}
