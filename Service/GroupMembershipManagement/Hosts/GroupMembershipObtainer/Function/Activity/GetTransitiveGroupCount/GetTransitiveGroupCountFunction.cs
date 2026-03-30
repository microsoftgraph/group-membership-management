// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class GetTransitiveGroupCountFunction
    {
        private readonly ILogger<GetTransitiveGroupCountFunction> _logger;
        private readonly SGMembershipCalculator _calculator;

        public GetTransitiveGroupCountFunction(ILogger<GetTransitiveGroupCountFunction> logger, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(GetTransitiveGroupCountFunction))]
        public async Task<int> GetGroupsAsync([ActivityTrigger] GetTransitiveGroupCountRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(GetTransitiveGroupCountFunction));
                var response = await _calculator.GetGroupsCountAsync(request.GroupId);
                _logger.FunctionCompleted(nameof(GetTransitiveGroupCountFunction));
                return response;
            }
        }
    }
}