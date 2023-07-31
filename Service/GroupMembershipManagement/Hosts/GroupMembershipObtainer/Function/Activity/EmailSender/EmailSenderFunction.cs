// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class EmailSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly SGMembershipCalculator _calculator = null;
        private readonly IEmailSenderRecipient _emailSenderRecipient = null;
        private const string EmailSubject = "EmailSubject";
        private const string SyncDisabledNoValidGroupIds = "SyncDisabledNoValidGroupIds";

        public EmailSenderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator, IEmailSenderRecipient emailSenderRecipient)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
            _emailSenderRecipient = emailSenderRecipient ?? throw new ArgumentNullException(nameof(emailSenderRecipient));
        }

        [FunctionName(nameof(EmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] EmailSenderRequest request)
        {
            if (request.SyncJob != null)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
                _calculator.RunId = request.RunId;
                await _calculator.SendEmailAsync(request.SyncJob, request.RunId, EmailSubject, SyncDisabledNoValidGroupIds, new[] { request.SyncJob.TargetOfficeGroupId.ToString(), request.SyncJob.Query, _emailSenderRecipient.SupportEmailAddresses }, request.AdaptiveCardTemplateDirectory);
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
            }
        }
    }
}