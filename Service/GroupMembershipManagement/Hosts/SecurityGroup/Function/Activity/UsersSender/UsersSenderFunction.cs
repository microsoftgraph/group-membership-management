// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.Helpers;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class UsersSenderFunction
    {
        private readonly ILoggingRepository _log;
        private readonly SGMembershipCalculator _calculator;

        public UsersSenderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
        {
            _log = loggingRepository;
            _calculator = calculator;
        }

        [FunctionName(nameof(UsersSenderFunction))]
        public async Task<string> SendUsersAsync([ActivityTrigger] UsersSenderRequest request)
        {
            string filePath = null;

            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(UsersSenderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);

            var users = JsonConvert.DeserializeObject<List<AzureADUser>>(TextCompressor.Decompress(request.Users));
            filePath = await _calculator.SendMembershipAsync(request.SyncJob, users, request.CurrentPart, request.Exclusionary);

            await _log.LogMessageAsync(new LogMessage
            {
                RunId = request.RunId,
                Message = $"Successfully uploaded {users.Count} users from source groups {request.SyncJob.Query} to blob storage to be put into the destination group {request.SyncJob.TargetOfficeGroupId}."
            });

            await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(UsersSenderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);

            return filePath;
        }
    }
}