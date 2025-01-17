// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Models.Helpers;
using Repositories.Contracts;
using Services.Contracts;
using SqlMembershipObtainer.SubOrchestrator;
using System;
using System.Linq;
using System.Text.Json;
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
        public async Task<GraphProfileInformationResponse> FilterChildEntities([ActivityTrigger] ChildEntitiesFilterRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ChildEntitiesFilterFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var filteredEntities = await _sqlMembershipObtainerService.FilterChildEntitiesAsync(request.Query, request.TableName, request.SyncJob.RunId, request.GroupId);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ChildEntitiesFilterFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var profiles = filteredEntities.Select(x => new GraphProfileInformation { PersonnelNumber = x.PersonnelNumber, Id = x.AzureObjectId }).Distinct().ToList();

            return new GraphProfileInformationResponse
            {
                GraphProfiles = TextCompressor.Compress(JsonSerializer.Serialize(profiles)),
                GraphProfileCount = profiles.Count
            };
        }
    }
}