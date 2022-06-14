// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
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
        public async Task SendEmailAsync([ActivityTrigger] SyncJobGroup group)
        {
            if (group != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function started", RunId = group.SyncJob.RunId }, VerbosityLevel.DEBUG);
                await _jobTriggerService.SendEmailAsync(group.SyncJob, group.Name);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function completed", RunId = group.SyncJob.RunId }, VerbosityLevel.DEBUG);
            }
        }
    }
}