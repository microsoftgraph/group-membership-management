// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using MessageSplitter.Contracts;
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;

namespace Hosts.MessageSplitter
{
    public class JobStatusUpdaterFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IMessageSplitterService _messageSplitterService;

        public JobStatusUpdaterFunction(ILoggingRepository loggingRepository, IMessageSplitterService messageSplitterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _messageSplitterService = messageSplitterService ?? throw new ArgumentNullException(nameof(messageSplitterService));
        }

        [Function(nameof(JobStatusUpdaterFunction))]
        public async Task UpdateJobStatusAsync([ActivityTrigger] JobStatusUpdaterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
            await _messageSplitterService.UpdateJobStatusAsync(request.SyncJob.Id, request.Status);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(JobStatusUpdaterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);
        }
    }
}