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
    public class ChildEntitiesFilterFunction
    {
        private readonly ILogger<ChildEntitiesFilterFunction> _logger;
        private readonly ISqlMembershipObtainerService _sqlMembershipObtainerService;

        public ChildEntitiesFilterFunction(ILogger<ChildEntitiesFilterFunction> logger, ISqlMembershipObtainerService sqlMembershipObtainerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sqlMembershipObtainerService = sqlMembershipObtainerService ?? throw new ArgumentNullException(nameof(sqlMembershipObtainerService));
        }

        [Function(nameof(ChildEntitiesFilterFunction))]
        public async Task<MembershipFileResult> FilterChildEntities([ActivityTrigger] ChildEntitiesFilterRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(ChildEntitiesFilterFunction));

                var response = await _sqlMembershipObtainerService.FilterChildEntitiesAsync(request.Query, request.TableName, request.SyncJob, request.GroupId, request.CurrentPart, request.Exclusionary);

                _logger.FunctionCompleted(nameof(ChildEntitiesFilterFunction));

                return response;
            }
        }
    }
}