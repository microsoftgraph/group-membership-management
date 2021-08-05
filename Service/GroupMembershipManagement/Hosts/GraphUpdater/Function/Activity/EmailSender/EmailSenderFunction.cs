// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class EmailSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphUpdaterService _graphUpdaterService;

        public EmailSenderFunction(ILoggingRepository loggingRepository, IGraphUpdaterService graphUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphUpdaterService = graphUpdaterService ?? throw new ArgumentNullException(nameof(graphUpdaterService)); ;
        }

        [FunctionName(nameof(EmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] EmailSenderRequest request, ILogger log)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function started", RunId = request.RunId });
            await _graphUpdaterService.SendEmailAsync(request.ToEmail, request.ContentTemplate, request.AdditionalContentParams, request.RunId, request.CcEmail);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function completed", RunId = request.RunId });
        }
    }
}