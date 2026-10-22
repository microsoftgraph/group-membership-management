// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
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

                try
                {
                    await _calculator.UploadCacheAsync(request.ObjectId, request.SyncJob.RunId.GetValueOrDefault(), request.MembershipFileResult, request.FileTimestamp);
                }
                // With a FileTimestamp the write-once upload already maps a lost race to success after
                // confirming the blob exists; an InvalidBlockList that reaches here then is a real failure.
                catch (RequestFailedException ex) when (ex.ErrorCode == "InvalidBlockList" && !request.FileTimestamp.HasValue)
                {
                    _logger.CacheUploadRaceConditionSkipped(request.ObjectId);
                }

                _logger.FunctionCompleted(nameof(CacheUploaderFunction));
            }
        }
    }
}