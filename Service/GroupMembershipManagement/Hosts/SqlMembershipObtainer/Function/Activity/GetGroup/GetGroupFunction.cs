// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.SqlMembershipObtainer;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SqlMembershipObtainer
{
    public class GetGroupFunction
    {
        private readonly ILogger<GetGroupFunction> _logger;
        private readonly ISqlMembershipObtainerService _sqlMembershipObtainerService;

        public GetGroupFunction(ILogger<GetGroupFunction> logger, ISqlMembershipObtainerService sqlMembershipObtainerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sqlMembershipObtainerService = sqlMembershipObtainerService ?? throw new ArgumentNullException(nameof(sqlMembershipObtainerService));
        }

        [Function(nameof(GetGroupFunction))]
        public async Task<Guid> GetGroupAsync([ActivityTrigger] GetGroupRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(GetGroupFunction));
                var groupId = await _sqlMembershipObtainerService.GetGroupIdAsync(request.SyncJob);
                _logger.FunctionCompleted(nameof(GetGroupFunction));
                return groupId;
            }
        }
    }
}