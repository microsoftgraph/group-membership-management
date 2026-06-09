// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class EmailSenderFunction
    {
        private readonly ILogger<EmailSenderFunction> _logger;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public EmailSenderFunction(ILogger<EmailSenderFunction> logger, IGraphUpdaterService graphUpdaterService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService));
        }

        [Function(nameof(EmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] EmailSenderRequest request)
        {
            using var scope = _logger.BeginGraphUpdaterScope(request);
            _logger.FunctionStarted(nameof(EmailSenderFunction));
            await _graphUpdaterService.SendEmailAsync(request.SyncJob, request.NotificationType, request.AdditionalContentParams);
            _logger.FunctionCompleted(nameof(EmailSenderFunction));
        }
    }
}