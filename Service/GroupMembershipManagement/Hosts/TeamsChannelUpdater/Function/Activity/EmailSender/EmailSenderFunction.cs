// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;
using Services.TeamsChannelUpdater.Contracts;

namespace Hosts.TeamsChannelUpdater
{
    public class EmailSenderFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ITeamsChannelUpdaterService _teamsChannelUpdaterService;

        public EmailSenderFunction(ILoggingRepository loggingRepository, ITeamsChannelUpdaterService teamsChannelUpdaterService)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _teamsChannelUpdaterService = teamsChannelUpdaterService ?? throw new ArgumentNullException(nameof(teamsChannelUpdaterService)); ;
        }

        [FunctionName(nameof(EmailSenderFunction))]
        public async Task SendEmailAsync([ActivityTrigger] EmailSenderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            await _teamsChannelUpdaterService.SendEmailAsync(request.ToEmail, request.ContentTemplate, request.AdditionalContentParams, request.RunId, request.CcEmail, null, null);
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(EmailSenderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}