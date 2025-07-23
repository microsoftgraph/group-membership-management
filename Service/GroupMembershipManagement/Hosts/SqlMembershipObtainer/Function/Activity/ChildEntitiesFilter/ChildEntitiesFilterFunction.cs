// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using SqlMembershipObtainer.Entities;
using System;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
{
    public class ChildEntitiesFilterFunction
    {
        private readonly ISqlMembershipObtainerService _sqlMembershipObtainerService = null;
        private readonly ILoggingRepository _loggingRepository = null;

        public ChildEntitiesFilterFunction(ISqlMembershipObtainerService sqlMembershipObtainerService, ILoggingRepository loggingRepository)
        {
            _sqlMembershipObtainerService = sqlMembershipObtainerService ?? throw new ArgumentNullException(nameof(sqlMembershipObtainerService));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(ChildEntitiesFilterFunction))]
        public async Task<GroupMembershipSenderResponse> FilterChildEntities([ActivityTrigger] ChildEntitiesFilterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ChildEntitiesFilterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var response = await _sqlMembershipObtainerService.FilterChildEntitiesAsync(request.Query, request.TableName, request.SyncJob, request.GroupId, request.CurrentPart, request.Exclusionary, request.AdaptiveCardTemplateDirectory);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ChildEntitiesFilterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            return response;
        }
    }
}