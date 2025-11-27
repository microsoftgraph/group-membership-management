// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class GetWarningJobs
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;

        public GetWarningJobs(ILoggingRepository loggingRepository,
            IAzureMaintenanceService azureMaintenanceService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(GetWarningJobs))]
        public async Task<List<SyncJob>> GetJobsApproachingDeletionAsync([ActivityTrigger] object input = null)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetWarningJobs)} function started" }, VerbosityLevel.DEBUG);
            
            var jobsApproachingDeletion = await _azureMaintenanceService.GetJobsApproachingPurgingAsync();
            
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GetWarningJobs)} function completed" }, VerbosityLevel.DEBUG);
            
            return jobsApproachingDeletion;
        }
    }
}
