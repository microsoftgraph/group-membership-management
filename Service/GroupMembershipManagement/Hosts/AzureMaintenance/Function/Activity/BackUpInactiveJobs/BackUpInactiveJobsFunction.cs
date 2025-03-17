// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
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
        public async Task<List<PurgedSyncJob>> BackupInactiveJobsAsync([ActivityTrigger] List<SyncJob> syncJobs)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(BackUpInactiveJobsFunction)} function started" }, VerbosityLevel.DEBUG);
            var backUpJobs = await _azureMaintenanceService.BackupInactiveJobsAsync(syncJobs);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(BackUpInactiveJobsFunction)} function completed" }, VerbosityLevel.DEBUG);
            return backUpJobs;
        }
    }
}