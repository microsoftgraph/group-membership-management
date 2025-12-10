// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using SqlMembershipObtainer.Entities;
using System;
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

        [Function(nameof(ManagerOrgReaderFunction))]
        public async Task<MembershipFileResult> ReadUsersAsync([ActivityTrigger] ManagerOrgReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ManagerOrgReaderFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            var response = await _sqlMembershipObtainerService.GetChildEntitiesAsync(request.Filter, request.PersonnelNumber, request.TableName, request.Depth, request.SyncJob, request.GroupId, request.CurrentPart, request.Exclusionary);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ManagerOrgReaderFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            return response;
        }
    }
}