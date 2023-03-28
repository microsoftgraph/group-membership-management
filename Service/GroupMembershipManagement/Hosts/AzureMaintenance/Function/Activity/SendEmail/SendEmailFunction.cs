// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class SendEmailFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;
        public SendEmailFunction(ILoggingRepository loggingRepository, IAzureMaintenanceService azureMaintenanceService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [FunctionName(nameof(SendEmailFunction))]
        public async Task SendEmailAsync([ActivityTrigger] SyncJob job)
        {
            if (job != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SendEmailFunction)} function started", RunId = job.RunId }, VerbosityLevel.DEBUG);
                var groupName = await _azureMaintenanceService.GetGroupNameAsync(job.TargetOfficeGroupId);
                if (string.IsNullOrEmpty(groupName)) return;
                await _azureMaintenanceService.SendEmailAsync(job, groupName);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SendEmailFunction)} function completed", RunId = job.RunId }, VerbosityLevel.DEBUG);
            }
        }
    }
}