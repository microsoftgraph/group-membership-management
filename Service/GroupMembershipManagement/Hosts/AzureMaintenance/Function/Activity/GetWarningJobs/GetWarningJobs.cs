// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class GetWarningJobs
    {
        private readonly ILogger<GetWarningJobs> _logger;
        private readonly IAzureMaintenanceService _azureMaintenanceService;

        public GetWarningJobs(ILogger<GetWarningJobs> logger,
            IAzureMaintenanceService azureMaintenanceService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(GetWarningJobs))]
        public async Task<List<SyncJob>> GetJobsApproachingDeletionAsync([ActivityTrigger] object input = null)
        {
            _logger.FunctionStarted(nameof(GetWarningJobs));
            
            var jobsApproachingDeletion = await _azureMaintenanceService.GetJobsApproachingPurgingAsync();
            
            _logger.FunctionCompleted(nameof(GetWarningJobs));
            
            return jobsApproachingDeletion;
        }
    }
}
