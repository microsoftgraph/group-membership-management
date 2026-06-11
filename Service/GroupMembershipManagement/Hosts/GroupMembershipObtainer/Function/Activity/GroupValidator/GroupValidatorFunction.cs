// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Models;
using Models.Notifications;
using Polly;
using Repositories.Contracts.InjectConfig;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class GroupValidatorFunction
    {
        private readonly ILogger<GroupValidatorFunction> _logger;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;
        private readonly SGMembershipCalculator _calculator;
        private const int NumberOfGraphRetries = 5;

        public GroupValidatorFunction(ILogger<GroupValidatorFunction> logger, SGMembershipCalculator calculator, IEmailSenderRecipient emailSenderAndRecipients)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = calculator;
            _emailSenderAndRecipients = emailSenderAndRecipients;
        }

        [Function(nameof(GroupValidatorFunction))]
        public async Task<bool> ValidateGroupAsync([ActivityTrigger] GroupValidatorRequest request)
        {
            bool isExistingGroup = false;
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(GroupValidatorFunction));
                try
                {
                    var groupExistsResult = await _calculator.GroupExistsAsync(request.ObjectId, request.SyncJob.RunId.GetValueOrDefault());
                    if (groupExistsResult.Outcome == OutcomeType.Successful && groupExistsResult.Result)
                    {
                        _logger.GroupExists(request.ObjectId);
                        isExistingGroup = true;
                    }
                    else
                    {
                        if (groupExistsResult.Outcome == OutcomeType.Successful)
                        {
                            _logger.GroupNotExists(request.ObjectId, SyncStatus.SecurityGroupNotFound.ToString());
                            var targetGroupName = await _calculator.GetGroupNameAsync(request.GroupId);
                            if (request.SyncJob != null && request.ObjectId != default(Guid))
                                await _calculator.SendEmailAsync(request.SyncJob,
                                                                    NotificationMessageType.SourceNotExistNotification,
                                                                    new[]
                                                                    {
                                                                    request.GroupId.ToString(),
                                                                    targetGroupName,
                                                                    request.ObjectId.ToString(),
                                                                    DisabledNotificationType.StatusDescriptions[NotificationMessageType.SourceNotExistNotification]
                                                                    });
                        }
                        else if (groupExistsResult.FaultType == FaultType.ExceptionHandledByThisPolicy)
                            _logger.GroupExistsRetriesExceeded(NumberOfGraphRetries);

                        if (groupExistsResult.FinalException != null) { throw groupExistsResult.FinalException; }
                        isExistingGroup = false;
                    }
                    _logger.FunctionCompleted(nameof(GroupValidatorFunction));
                    return isExistingGroup;
                }
                catch (MsalClientException ex)
                {
                    if (ex.ErrorCode == "MULTIPLE_MATCHING_TOKENS_DETECTED")
                    {
                        _logger.GroupValidatorTransientError(ex.Message);
                        await _calculator.UpdateSyncJobStatusAsync(request.SyncJob, SyncStatus.TransientError);
                    }
                    throw;
                }
            }
        }
    }
}