// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class CacheUploaderFunction
    {
        private readonly ILoggingRepository _log;
        private readonly SGMembershipCalculator _calculator;

        public CacheUploaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
        {
            _log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [FunctionName(nameof(CacheUploaderFunction))]
        public async Task SendUsersAsync([ActivityTrigger] CacheUploaderRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(CacheUploaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            await _calculator.UploadCacheAsync(request.ObjectId, request.FilePath);
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(CacheUploaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}