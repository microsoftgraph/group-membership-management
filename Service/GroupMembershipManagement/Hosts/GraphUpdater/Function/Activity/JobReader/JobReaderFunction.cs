// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class JobReaderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public JobReaderFunction(ILoggingRepository loggingRepository, IGraphUpdaterService graphUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        [FunctionName(nameof(JobReaderFunction))]
        public async Task<SyncJob> LogMessageAsync([ActivityTrigger] JobReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobReaderFunction)} function started", RunId = request.RunId });
            var syncJob = await _graphUpdaterService.GetSyncJobAsync(request.JobPartitionKey, request.JobRowKey);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobReaderFunction)} function started", RunId = request.RunId });
            return syncJob;
        }
    }
}
