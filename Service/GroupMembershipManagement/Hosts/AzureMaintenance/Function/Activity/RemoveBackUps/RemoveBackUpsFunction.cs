// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class RemoveBackUpsFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;
        public RemoveBackUpsFunction(ILoggingRepository loggingRepository, IAzureMaintenanceService azureMaintenanceService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [FunctionName(nameof(RemoveBackUpsFunction))]
        public async Task<int> RemoveBackUpsAsync([ActivityTrigger] object obj)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(RemoveBackUpsFunction)} function started" }, VerbosityLevel.DEBUG);
            int countOfRemovedJobs =  await _azureMaintenanceService.RemoveBackupsAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(RemoveBackUpsFunction)} function completed" }, VerbosityLevel.DEBUG);
            return countOfRemovedJobs;
        }
    }
}