// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class ReadGroupNameFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;
        public ReadGroupNameFunction(ILoggingRepository loggingRepository, IAzureMaintenanceService azureMaintenanceService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService)); ;
        }

        [FunctionName(nameof(ReadGroupNameFunction))]
        public async Task<SyncJobGroup> GetGroupNameAsync([ActivityTrigger] SyncJob syncJob)
        {
            var group = new SyncJobGroup();
            if (syncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ReadGroupNameFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
                var groupName = await _azureMaintenanceService.GetGroupNameAsync(syncJob.TargetOfficeGroupId);
                group.SyncJob = syncJob;
                group.Name = groupName;
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ReadGroupNameFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);
            }
            return group;
        }
    }
}