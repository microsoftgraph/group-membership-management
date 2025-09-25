// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class CacheConverterFunction
    {
        private readonly ILoggingRepository _log;
        private readonly SGMembershipCalculator _calculator;

        public CacheConverterFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
        {
            _log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [Function(nameof(CacheConverterFunction))]
        public async Task SendUsersAsync([ActivityTrigger] CacheConverterRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(CacheConverterFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            // Reuse the UploadCacheAsync method to convert the cache file to parquet format
            await _calculator.UploadCacheAsync(request.ObjectId, request.FilePath);
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(CacheConverterFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}