// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class EmailSenderFunction
    {
        private readonly ILogger<EmailSenderFunction> _logger;
        private readonly SGMembershipCalculator _calculator = null;

        public EmailSenderFunction(ILogger<EmailSenderFunction> logger, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(EmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] EmailSenderRequest request)
        {
            if (request.SyncJob != null)
            {
                using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
                {
                    _logger.FunctionStarted(nameof(EmailSenderFunction));
                    await _calculator.SendEmailAsync(request.SyncJob, request.NotificationType, request.AdditionalContentParams);
                    _logger.FunctionCompleted(nameof(EmailSenderFunction));
                }
            }
        }
    }
}