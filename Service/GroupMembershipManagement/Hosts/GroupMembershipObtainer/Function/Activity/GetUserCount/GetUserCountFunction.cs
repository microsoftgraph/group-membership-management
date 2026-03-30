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
    public class GetUserCountFunction
    {
        private readonly ILogger<GetUserCountFunction> _logger;
        private readonly SGMembershipCalculator _calculator;

        public GetUserCountFunction(ILogger<GetUserCountFunction> logger, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(GetUserCountFunction))]
        public async Task<int> GetUserCountAsync([ActivityTrigger] GetUserCountRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(GetUserCountFunction));
                var response = await _calculator.GetUsersCountAsync(request.GroupId);
                _logger.FunctionCompleted(nameof(GetUserCountFunction));
                return response;
            }
        }
    }
}