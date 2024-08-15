// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using JobTrigger.Activity.EmailSender;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.JobTrigger
{
    public class EmailSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IJobTriggerService _jobTriggerService = null;
        public EmailSenderFunction(ILoggingRepository loggingRepository, IJobTriggerService jobTriggerService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService)); ;
        }

        [Function(nameof(EmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] EmailSenderRequest request)
        {
            var job = request.SyncJob;
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function started", RunId = job.RunId }, VerbosityLevel.DEBUG);
            _jobTriggerService.RunId = job.RunId ?? Guid.Empty;
            await _jobTriggerService.SendEmailAsync(job, request.NotificationType, request.AdditionalContentParams);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function completed", RunId = job.RunId }, VerbosityLevel.DEBUG);
        }
    }
}