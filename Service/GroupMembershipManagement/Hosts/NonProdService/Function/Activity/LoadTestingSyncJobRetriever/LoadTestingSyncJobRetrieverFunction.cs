// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Repositories.EntityFramework;
using System;
using System.Threading.Tasks;

namespace Hosts.NonProdService
{
    public class LoadTestingSyncJobRetrieverFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository = null;

        public LoadTestingSyncJobRetrieverFunction(ILoggingRepository loggingRepository, IDatabaseSyncJobsRepository databaseSyncJobsRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
        }

        [FunctionName(nameof(LoadTestingSyncJobRetrieverFunction))]
        public async Task<LoadTestingSyncJobRetrieverResponse> GenerateGroup([ActivityTrigger] LoadTestingSyncJobRetrieverRequest request, ILogger log)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(LoadTestingSyncJobRetrieverFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var syncJobs = await _databaseSyncJobsRepository.GetSyncJobsAsync();

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(LoadTestingSyncJobRetrieverFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return new LoadTestingSyncJobRetrieverResponse
            {
                SyncJobs = syncJobs
            };
        }
    }
}
