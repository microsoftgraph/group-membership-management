// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Graph;
using Models;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Services
{
    public class GraphAPIService : IGraphAPIService
    {
        private const int NumberOfGraphRetries = 5;
        private const string EmailSubject = "EmailSubject";
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IMailRepository _mailRepository;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;

        private Guid _runId;
        public Guid RunId
        {
            get { return _runId; }
            set
            {
                _runId = value;
                _graphGroupRepository.RunId = value;
            }
        }

        public GraphAPIService(
                ILoggingRepository loggingRepository,
                IGraphGroupRepository graphGroupRepository,
                IMailRepository mailRepository,
                IEmailSenderRecipient emailSenderAndRecipients)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _mailRepository = mailRepository ?? throw new ArgumentNullException(nameof(mailRepository));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
        }

        public async Task<string> GetGroupNameAsync(Guid groupId)
        {
            return await _graphGroupRepository.GetGroupNameAsync(groupId);
        }

        public async Task<PolicyResult<bool>> GroupExistsAsync(Guid groupId, Guid runId)
        {
            var graphRetryPolicy = Policy.Handle<SocketException>()
                                    .WaitAndRetryAsync(NumberOfGraphRetries, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                   onRetry: async (ex, count) =>
                   {
                       await _loggingRepository.LogMessageAsync(new LogMessage
                       {
                           Message = $"Got a transient SocketException. Retrying. This was try {count} out of {NumberOfGraphRetries}.\n" + ex.ToString(),
                           RunId = runId
                       });
                   });

            return await graphRetryPolicy.ExecuteAndCaptureAsync(() => _graphGroupRepository.GroupExists(groupId));
        }

        public async Task<List<User>> GetGroupOwnersAsync(Guid groupObjectId, int top = 0)
        {
            return await _graphGroupRepository.GetGroupOwnersAsync(groupObjectId, top);
        }

        public async Task<bool> IsEmailRecipientOwnerOfGroupAsync(string email, Guid groupObjectId)
        {
            return await _graphGroupRepository.IsEmailRecipientOwnerOfGroupAsync(email, groupObjectId);
        }

        public async Task SendEmailAsync(string toEmail,
                                         string contentTemplate,
                                         string[] additionalContentParams,
                                         Guid runId,
                                         string ccEmail = null,
                                         string emailSubject = null,
                                         string[] additionalSubjectParams = null)
        {
            await _mailRepository.SendMailAsync(new EmailMessage
            {
                Subject = emailSubject ?? EmailSubject,
                Content = contentTemplate,
                SenderAddress = _emailSenderAndRecipients.SenderAddress,
                SenderPassword = _emailSenderAndRecipients.SenderPassword,
                ToEmailAddresses = toEmail,
                CcEmailAddresses = ccEmail,
                AdditionalContentParams = additionalContentParams,
                AdditionalSubjectParams = additionalSubjectParams
            }, runId);
        }
    }
}