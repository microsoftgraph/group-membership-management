// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using MessageSplitter.Contracts;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.Threading.Tasks;

namespace Hosts.MessageSplitter
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILogger<JobStatusUpdaterFunction> _logger;
        private readonly IMessageSplitterService _messageSplitterService;

        public JobStatusUpdaterFunction(ILogger<JobStatusUpdaterFunction> logger, IMessageSplitterService messageSplitterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _messageSplitterService = messageSplitterService ?? throw new ArgumentNullException(nameof(messageSplitterService));
        }

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob))
            {
                _logger.FunctionStarted(nameof(JobStatusUpdaterFunction));
                await _messageSplitterService.UpdateJobStatusAsync(request.SyncJob.Id, request.Status);
                _logger.FunctionCompleted(nameof(JobStatusUpdaterFunction));
            }
        }
    }
}