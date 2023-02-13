// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using JobTrigger.Activity.EmailSender;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Threading.Tasks;

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

        [FunctionName(nameof(EmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] EmailSenderRequest request)
        {
            var job = request.SyncJobGroup.SyncJob;
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function started", RunId = job.RunId }, VerbosityLevel.DEBUG);
            _jobTriggerService.RunId = job.RunId ?? Guid.Empty;
            await _jobTriggerService.SendEmailAsync(job, request.EmailSubjectTemplateName, request.EmailContentTemplateName, request.AdditionalContentParams, request.FunctionDirectory);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function completed", RunId = job.RunId }, VerbosityLevel.DEBUG);

        }
    }
}