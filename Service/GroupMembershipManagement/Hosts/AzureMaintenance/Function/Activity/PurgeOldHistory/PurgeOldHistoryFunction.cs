// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.AzureMaintenance
{
    public class PurgeOldHistoryFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IAzureMaintenanceService _azureMaintenanceService;
        public PurgeOldHistoryFunction(ILoggingRepository loggingRepository, IAzureMaintenanceService azureMaintenanceService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [Function(nameof(PurgeOldHistoryFunction))]
        public async Task<int> PurgeOldHistoryAsync([ActivityTrigger] object obj)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PurgeOldHistoryFunction)} function started" }, VerbosityLevel.DEBUG);
            int countOfDeletedRecords = await _azureMaintenanceService.PurgeOldHistoryAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(PurgeOldHistoryFunction)} function completed" }, VerbosityLevel.DEBUG);
            return countOfDeletedRecords;
        }
    }
}
