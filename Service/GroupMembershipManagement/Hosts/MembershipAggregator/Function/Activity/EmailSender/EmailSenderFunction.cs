// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using MembershipAggregator.Activity.EmailSender;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.MembershipAggregator
{
    public class EmailSenderFunction
    {
        private readonly ILogger<EmailSenderFunction> _logger;
        private readonly IGraphAPIService _graphAPIService;

        public EmailSenderFunction(ILogger<EmailSenderFunction> logger, IGraphAPIService graphAPIService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphAPIService = graphAPIService ?? throw new ArgumentNullException(nameof(graphAPIService));
        }

        [Function(nameof(EmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] EmailSenderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(EmailSenderFunction));
                await _graphAPIService.SendEmailAsync(request.SyncJob, request.NotificationType, request.AdditionalContentParams);
                _logger.FunctionCompleted(nameof(EmailSenderFunction));
            }
        }
    }
}