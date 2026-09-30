// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.SqlMembershipObtainer;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using SqlMembershipObtainer.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
{
    public class ManagerOrgReaderFunction
    {
        private readonly ILogger<ManagerOrgReaderFunction> _logger;
        private readonly ISqlMembershipObtainerService _sqlMembershipObtainerService;

        public ManagerOrgReaderFunction(ILogger<ManagerOrgReaderFunction> logger, ISqlMembershipObtainerService sqlMembershipObtainerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sqlMembershipObtainerService = sqlMembershipObtainerService ?? throw new ArgumentNullException(nameof(sqlMembershipObtainerService));
        }

        [Function(nameof(ManagerOrgReaderFunction))]
        public async Task<MembershipFileResult> ReadUsersAsync([ActivityTrigger] ManagerOrgReaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(ManagerOrgReaderFunction));

                var evaluationRequest =
                    SqlMembershipEvaluationRequest.FromIdentityTypeNames(
                        request.PersonnelNumber,
                        request.Depth,
                        request.Filter,
                        request.IdentityTypes,
                        request.TableName);
                var evaluation =
                    await _sqlMembershipObtainerService.EvaluateMembershipAsync(
                        evaluationRequest,
                        request.SyncJob,
                        request.GroupId,
                        request.CurrentPart);
                var response =
                    await _sqlMembershipObtainerService.UploadMembershipEvaluationAsync(
                        evaluation,
                        request.SyncJob,
                        request.GroupId,
                        request.CurrentPart,
                        request.Exclusionary);

                _logger.FunctionCompleted(nameof(ManagerOrgReaderFunction));

                return response;
            }
        }
    }
}