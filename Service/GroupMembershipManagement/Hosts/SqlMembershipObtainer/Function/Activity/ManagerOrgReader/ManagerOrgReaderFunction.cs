// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.Helpers;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Newtonsoft.Json;
using SqlMembershipObtainer.SubOrchestrator;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
{
    public class ManagerOrgReaderFunction
    {
        private readonly ISqlMembershipObtainerService _sqlMembershipObtainerService = null;
        private readonly ILoggingRepository _loggingRepository = null;

        public ManagerOrgReaderFunction(ISqlMembershipObtainerService sqlMembershipObtainerService, ILoggingRepository loggingRepository)
        {
            _sqlMembershipObtainerService = sqlMembershipObtainerService ?? throw new ArgumentNullException(nameof(sqlMembershipObtainerService));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(ManagerOrgReaderFunction))]
        public async Task<GraphProfileInformationResponse> ReadUsersAsync([ActivityTrigger] ManagerOrgReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ManagerOrgReaderFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var response = await _sqlMembershipObtainerService.GetChildEntitiesAsync(request.Filter, request.PersonnelNumber, request.TableName, request.Depth, request.SyncJob.RunId, request.SyncJob.TargetOfficeGroupId);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ManagerOrgReaderFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var profiles = response.Select(x => new GraphProfileInformation { PersonnelNumber = x.RowKey, Id = x.AzureObjectId }).ToList();

            return new GraphProfileInformationResponse
            {
                GraphProfiles = TextCompressor.Compress(JsonConvert.SerializeObject(profiles)),
                GraphProfileCount = profiles.Count
            };
        }
    }
}