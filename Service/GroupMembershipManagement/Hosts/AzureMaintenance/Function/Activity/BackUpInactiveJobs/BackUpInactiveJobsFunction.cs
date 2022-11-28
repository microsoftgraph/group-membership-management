// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class BackUpInactiveJobsFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;
        public BackUpInactiveJobsFunction(ILoggingRepository loggingRepository, IAzureMaintenanceService azureMaintenanceService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [FunctionName(nameof(BackUpInactiveJobsFunction))]
        public async Task<int> BackupInactiveJobsAsync([ActivityTrigger] List<SyncJob> syncJobs)
        {
            int countOfBackUpJobs = 0;
            if (syncJobs.Count > 0)
            {
                countOfBackUpJobs = await _azureMaintenanceService.BackupInactiveJobsAsync(syncJobs);
            }
            return countOfBackUpJobs;
        }
    }
}