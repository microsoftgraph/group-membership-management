// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using MembershipAggregator.Activity.EmailSender;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class EmailSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IGraphAPIService _graphAPIService  = null;

        public EmailSenderFunction(ILoggingRepository loggingRepository, IGraphAPIService graphAPIService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphAPIService = graphAPIService ?? throw new ArgumentNullException(nameof(graphAPIService)); ;
        }

        [FunctionName(nameof(EmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] EmailSenderRequest request)
        {
            var job = request.SyncJob;
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function started", RunId = job.RunId }, VerbosityLevel.DEBUG);
            _graphAPIService.RunId = job.RunId ?? Guid.Empty;
            await _graphAPIService.SendEmailAsync(job, request.NotificationType, request.AdditionalContentParams);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function completed", RunId = job.RunId }, VerbosityLevel.DEBUG);

        }
    }
}
       