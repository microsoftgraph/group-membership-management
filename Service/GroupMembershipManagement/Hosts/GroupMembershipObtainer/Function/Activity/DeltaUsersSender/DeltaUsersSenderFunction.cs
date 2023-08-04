// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.Helpers;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class DeltaUsersSenderFunction
    {
        private readonly ILoggingRepository _log;
        private readonly SGMembershipCalculator _calculator;

        public DeltaUsersSenderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
        {
            _log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
        }

        [FunctionName(nameof(DeltaUsersSenderFunction))]
        public async Task SendUsersAsync([ActivityTrigger] DeltaUsersSenderRequest request)
        {
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaUsersSenderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            
            var users = JsonConvert.DeserializeObject<List<AzureADUser>>(TextCompressor.Decompress(request.CompressedUsers));
            await _calculator.SaveDeltaUsersAsync(request.SyncJob, request.ObjectId, users, request.DeltaLink);
            await _log.LogMessageAsync(new LogMessage
            {
                RunId = request.RunId,
                Message = $"Successfully uploaded {users.Count} users from group {request.ObjectId} + delta link {request.DeltaLink} to cache."
            });
            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaUsersSenderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
        }
    }
}