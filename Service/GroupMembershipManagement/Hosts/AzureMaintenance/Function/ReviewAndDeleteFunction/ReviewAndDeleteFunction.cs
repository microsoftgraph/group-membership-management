// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System;
using System.Threading.Tasks;
using Services.Contracts;
using Repositories.Contracts;
using Services.Entities;

namespace Hosts.AzureMaintenance
{
    public class ReviewAndDeleteFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IAzureMaintenanceService _azureMaintenanceService = null;
        public ReviewAndDeleteFunction(IAzureMaintenanceService azureMaintenanceService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureMaintenanceService = azureMaintenanceService ?? throw new ArgumentNullException(nameof(azureMaintenanceService));
        }

        [FunctionName(nameof(ReviewAndDeleteFunction))]
        public async Task<bool> ReviewAndDelete([ActivityTrigger] ReviewAndDeleteRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ReviewAndDeleteFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            var entityDeleted = await _azureMaintenanceService.ReviewAndDeleteAsync(request.MaintenanceSetting, request.TableName);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ReviewAndDeleteFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            return entityDeleted;
        }
    }
}
