// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.TeamsChannelUpdater.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.TeamsChannelUpdater
{
    public class EmailSenderFunction
    {
        private readonly ILogger<EmailSenderFunction> _logger;
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;

        public EmailSenderFunction(ILogger<EmailSenderFunction> logger, ITeamsChannelUpdaterService teamsChannelUpdaterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService));
        }

        [Function(nameof(EmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] EmailSenderRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.SyncJob);

            _logger.FunctionStarted(nameof(EmailSenderFunction));
            await _teamsChannelUpdaterService.SendEmailAsync(request.SyncJob, request.NotificationType, request.AdditionalContentParams);
            _logger.FunctionCompleted(nameof(EmailSenderFunction));
        }
    }
}
