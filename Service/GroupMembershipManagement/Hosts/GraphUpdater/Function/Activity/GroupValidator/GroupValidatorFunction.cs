// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Identity.Client;

namespace Hosts.GraphUpdater
{
    public class GroupValidatorFunction
    {
        private const int NumberOfGraphRetries = 5;
        private const string SyncDisabledNoGroupEmailBody = "SyncDisabledNoGroupEmailBody";
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphUpdaterService _graphUpdaterService;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;

        public GroupValidatorFunction(ILoggingRepository loggingRepository, IGraphUpdaterService graphUpdaterService, IEmailSenderRecipient emailSenderAndRecipients)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
        }

        [FunctionName(nameof(GroupValidatorFunction))]
        public async Task<bool> ValidateGroupAsync([ActivityTrigger] GroupValidatorRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupValidatorFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            _graphUpdaterService.RunId = request.RunId;
            var syncJob = await _graphUpdaterService.GetSyncJobAsync(request.JobId);

            try
            {

            var groupExistsResult = await _graphUpdaterService.GroupExistsAsync(request.GroupId, request.RunId);

                if (groupExistsResult)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Group with ID {request.GroupId} exists." });
                }
                else
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Group with ID {request.GroupId} doesn't exist." });

                    if (syncJob != null)
                        await _graphUpdaterService.SendEmailAsync(
                            syncJob.Requestor,
                            SyncDisabledNoGroupEmailBody,
                            new[] { request.GroupId.ToString(), _emailSenderAndRecipients.SupportEmailAddresses },
                            request.RunId, null, null, null, request.AdaptiveCardTemplateDirectory);
                }

                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupValidatorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
                return groupExistsResult;
            }
            catch (MsalClientException ex)
            {
                if (ex.ErrorCode == "MULTIPLE_MATCHING_TOKENS_DETECTED")
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Marking sync job status as transient error. Exception:\n{ex.Message}", RunId = request.RunId });
                    await _graphUpdaterService.UpdateSyncJobStatusAsync(syncJob, SyncStatus.TransientError, false, request.RunId);
                }
                throw;
            }
        }
    }
}