// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;
using Services.Entities;

namespace Hosts.AzureMaintenance
{
    public class RetrieveBackupsFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;
        public RetrieveBackupsFunction(IAzureMaintenanceService azureMaintenanceService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [FunctionName(nameof(RetrieveBackupsFunction))]
        public async Task<List<ReviewAndDeleteRequest>> RetrieveBackups([ActivityTrigger] object request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(RetrieveBackupsFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            var backups = await _azureMaintenanceService.RetrieveBackupsAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(RetrieveBackupsFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            return backups.Select(e => new ReviewAndDeleteRequest() { TableName = e.TableName, MaintenanceSetting = e.MaintenanceSetting }).ToList();
        }
    }
}
