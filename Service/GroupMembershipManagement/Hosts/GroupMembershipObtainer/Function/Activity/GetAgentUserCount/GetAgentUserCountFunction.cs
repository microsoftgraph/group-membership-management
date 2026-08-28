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
    public class GetAgentUserCountFunction
    {
        private readonly ILogger<GetAgentUserCountFunction> _logger;
        private readonly SGMembershipCalculator _calculator;

        public GetAgentUserCountFunction(ILogger<GetAgentUserCountFunction> logger, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(GetAgentUserCountFunction))]
        public async Task<int> GetAgentUserCountAsync([ActivityTrigger] GetAgentUserCountRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(GetAgentUserCountFunction));
                var response = await _calculator.GetAgentUserCountAsync(request.GroupId);
                _logger.FunctionCompleted(nameof(GetAgentUserCountFunction));
                return response;
            }
        }
    }
}
