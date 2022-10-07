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
        private readonly IAzureMaintenanceService _azureTableBackupService = null;
        public ReviewAndDeleteFunction(IAzureMaintenanceService azureTableBackupService, ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _azureTableBackupService = azureTableBackupService ?? throw new ArgumentNullException(nameof(azureTableBackupService));
        }

        [FunctionName(nameof(ReviewAndDeleteFunction))]
        public async Task<bool> ReviewAndDelete([ActivityTrigger] ReviewAndDeleteRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ReviewAndDeleteFunction)} function started at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);
            var entityDeleted = await _azureTableBackupService.ReviewAndDeleteAsync(request.BackupSetting, request.TableName);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ReviewAndDeleteFunction)} function completed at: {DateTime.UtcNow}" }, VerbosityLevel.DEBUG);

            return entityDeleted;
        }
    }
}
