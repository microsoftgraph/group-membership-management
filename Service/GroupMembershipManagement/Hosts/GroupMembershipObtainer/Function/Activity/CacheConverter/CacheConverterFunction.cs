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
    public class CacheConverterFunction
    {
        private readonly ILogger<CacheConverterFunction> _logger;
        private readonly SGMembershipCalculator _calculator;

        public CacheConverterFunction(ILogger<CacheConverterFunction> logger, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(CacheConverterFunction))]
        public async Task SendUsersAsync([ActivityTrigger] CacheConverterRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(CacheConverterFunction));
                // Reuse the UploadCacheAsync method to convert the cache file to parquet format
                var gmfResult = new GroupMembershipFileResult { FilePath = request.FilePath };
                await _calculator.UploadCacheAsync(request.ObjectId, request.SyncJob.RunId.GetValueOrDefault(), gmfResult);
                _logger.FunctionCompleted(nameof(CacheConverterFunction));
            }
        }
    }
}