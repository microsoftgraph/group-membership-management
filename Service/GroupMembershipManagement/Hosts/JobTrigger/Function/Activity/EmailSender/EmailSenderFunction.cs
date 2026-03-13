// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using JobTrigger.Activity.EmailSender;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class EmailSenderFunction
    {
        private readonly ILogger<EmailSenderFunction> _logger;
        private readonly IJobTriggerService _jobTriggerService;

        public EmailSenderFunction(ILogger<EmailSenderFunction> logger, IJobTriggerService jobTriggerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _jobTriggerService = jobTriggerService ?? throw new ArgumentNullException(nameof(jobTriggerService));
        }

        [Function(nameof(EmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] EmailSenderRequest request)
        {
            var job = request.SyncJob;
            using var activity = CorrelationActivity.StartSyncJobActivity(nameof(EmailSenderFunction), job);
            using (_logger.BeginSyncJobScope(job))
            {
                _logger.ActivityFunctionStarted(nameof(EmailSenderFunction));
                await _jobTriggerService.SendEmailAsync(job, request.NotificationType, request.AdditionalContentParams);
                _logger.ActivityFunctionCompleted(nameof(EmailSenderFunction));
            }
        }
    }
}
