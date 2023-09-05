// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Identity.Client;
using Models;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Threading.Tasks;
using static System.Net.WebRequestMethods;

namespace Hosts.GroupMembershipObtainer
{
    public class GroupValidatorFunction
    {
        private readonly ILoggingRepository _log;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly SGMembershipCalculator _calculator;
        private const int NumberOfGraphRetries = 5;
        private const string DisabledJobEmailSubject = "DisabledJobEmailSubject";
        private const string SyncDisabledNoGroupEmailBody = "SyncDisabledNoSourceGroupEmailBody";

        public GroupValidatorFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator, IEmailSenderRecipient emailSenderAndRecipients)
        {
            _log = loggingRepository;
            _calculator = calculator;
            _emailSenderAndRecipients = emailSenderAndRecipients;
        }

        [FunctionName(nameof(GroupValidatorFunction))]
        public async Task<bool> ValidateGroupAsync([ActivityTrigger] GroupValidatorRequest request)
        {
            bool isExistingGroup = false;
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupValidatorFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            _calculator.RunId = request.RunId;
            try
            {
                var groupExistsResult = await _calculator.GroupExistsAsync(request.ObjectId, request.RunId);
                if (groupExistsResult.Outcome == OutcomeType.Successful && groupExistsResult.Result)
                {
                    await _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Group with ID {request.ObjectId} exists." });
                    isExistingGroup = true;
                }
                else
                {
                    if (groupExistsResult.Outcome == OutcomeType.Successful)
                    {
                        await _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Group with ID {request.ObjectId} doesn't exist. Stopping sync and marking as {SyncStatus.SecurityGroupNotFound}." });
                        var targetGroupName = await _calculator.GetGroupNameAsync(request.SyncJob.TargetOfficeGroupId);
                        if (request.SyncJob != null && request.ObjectId != default(Guid))
                            await _calculator.SendEmailAsync(request.SyncJob,
                                                                request.RunId,
                                                                DisabledJobEmailSubject,
                                                                SyncDisabledNoGroupEmailBody,
                                                                new[]
                                                                {
                                                                targetGroupName,
                                                                request.ObjectId.ToString(),
                                                                _emailSenderAndRecipients.SyncDisabledCCAddresses
                                                                }, request.AdaptiveCardTemplateDirectory);
                    }
                    else if (groupExistsResult.FaultType == FaultType.ExceptionHandledByThisPolicy)
                        await _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Exceeded {NumberOfGraphRetries} while trying to determine if a group exists. Stopping sync and marking as error." });

                    if (groupExistsResult.FinalException != null) { throw groupExistsResult.FinalException; }
                    isExistingGroup = false;
                }
                await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupValidatorFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
                return isExistingGroup;
            }
            catch (MsalClientException ex)
            {
                if (ex.ErrorCode == "MULTIPLE_MATCHING_TOKENS_DETECTED")
                {
                    await _log.LogMessageAsync(new LogMessage { Message = $"Marking sync job status as transient error. Exception:\n{ex.Message}", RunId = request.RunId });
                    await _calculator.UpdateSyncJobStatusAsync(request.SyncJob, SyncStatus.TransientError);
                }
                throw;
            }
        }
    }
}