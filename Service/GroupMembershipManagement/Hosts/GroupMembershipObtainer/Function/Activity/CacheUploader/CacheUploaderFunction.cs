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
    public class CacheUploaderFunction
    {
        private readonly ILogger<CacheUploaderFunction> _logger;
        private readonly SGMembershipCalculator _calculator;

        public CacheUploaderFunction(ILogger<CacheUploaderFunction> logger, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(CacheUploaderFunction))]
        public async Task SendUsersAsync([ActivityTrigger] CacheUploaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(CacheUploaderFunction));
                await _calculator.UploadCacheAsync(request.ObjectId, request.SyncJob.RunId.GetValueOrDefault(), request.MembershipFileResult);
                _logger.FunctionCompleted(nameof(CacheUploaderFunction));
            }
        }
    }
}