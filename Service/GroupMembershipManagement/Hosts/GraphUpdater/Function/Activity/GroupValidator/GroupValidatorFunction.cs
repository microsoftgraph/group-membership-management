// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models.Notifications;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class GroupValidatorFunction
    {
        private const int NumberOfGraphRetries = 5;
        private readonly ILogger<GroupValidatorFunction> _logger;
        private readonly IGraphUpdaterService _graphUpdaterService;
        private readonly IEmailSenderRecipient _emailSenderAndRecipients;

        public GroupValidatorFunction(ILogger<GroupValidatorFunction> logger, IGraphUpdaterService graphUpdaterService, IEmailSenderRecipient emailSenderAndRecipients)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
            _emailSenderAndRecipients = emailSenderAndRecipients ?? throw new ArgumentNullException(nameof(emailSenderAndRecipients));
        }

        [Function(nameof(GroupValidatorFunction))]
        public async Task<bool> ValidateGroupAsync([ActivityTrigger] GroupValidatorRequest request)
        {
            using var scope = _logger.BeginGraphUpdaterScope(request);
            _logger.FunctionStarted(nameof(GroupValidatorFunction));

            var groupExistsResult = await _graphUpdaterService.GroupExistsAsync(request.GroupId);

            if (groupExistsResult)
            {
                _logger.GroupExists(request.GroupId);
            }
            else
            {
                _logger.GroupNotExists(request.GroupId);
                var syncJob = await _graphUpdaterService.GetSyncJobAsync(request.SyncJob.Id);
                if (syncJob != null)
                    await _graphUpdaterService.SendEmailAsync(
                        syncJob,
                        NotificationMessageType.DestinationNotExistNotification,
                        new[] { request.GroupId.ToString(), _emailSenderAndRecipients.SupportEmailAddresses, DisabledNotificationType.StatusDescriptions[NotificationMessageType.DestinationNotExistNotification] }
                        );
            }

            _logger.FunctionCompleted(nameof(GroupValidatorFunction));
            return groupExistsResult;
        }
    }
}