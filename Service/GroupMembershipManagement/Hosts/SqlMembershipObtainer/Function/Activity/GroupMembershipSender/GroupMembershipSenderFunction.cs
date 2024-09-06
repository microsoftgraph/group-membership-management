// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Models.Helpers;
using Newtonsoft.Json;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
{
    public class GroupMembershipSenderFunction
    {
        private readonly ISqlMembershipObtainerService _sqlMembershipObtainerService = null;
        private readonly ILoggingRepository _loggingRepository = null;

        public GroupMembershipSenderFunction(ISqlMembershipObtainerService sqlMembershipObtainerService, ILoggingRepository loggingRepository)
        {
            _sqlMembershipObtainerService = sqlMembershipObtainerService ?? throw new ArgumentNullException(nameof(sqlMembershipObtainerService));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(GroupMembershipSenderFunction))]
        public async Task<OrchestratorResponse> SendGroupMembershipAsync([ActivityTrigger] GroupMembershipSenderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupMembershipSenderFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var profiles = JsonConvert.DeserializeObject<List<GraphProfileInformation>>(TextCompressor.Decompress(request.Profiles));
            var response = await _sqlMembershipObtainerService.SendGroupMembershipAsync(profiles, request.SyncJob, request.CurrentPart, request.Exclusionary, request.AdaptiveCardTemplateDirectory);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupMembershipSenderFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            return new OrchestratorResponse
            {
                Status = response.Status,
                FilePath = response.FilePath
            };
        }
    }
}
