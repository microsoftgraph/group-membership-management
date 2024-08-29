// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.AzureMaintenance
{
    public class ReadSyncJobsFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;
        public ReadSyncJobsFunction(ILoggingRepository loggingRepository, IAzureMaintenanceService azureMaintenanceService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(ReadSyncJobsFunction))]
        public async Task<List<SyncJob>> GetSyncJobsAsync([ActivityTrigger] object obj)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ReadSyncJobsFunction)} function started" }, VerbosityLevel.DEBUG);
            var jobs = await _azureMaintenanceService.GetSyncJobsAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ReadSyncJobsFunction)} function completed" }, VerbosityLevel.DEBUG);
            return jobs;
        }
    }
}