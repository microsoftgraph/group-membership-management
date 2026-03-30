// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Models.Helpers;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class UsersSenderFunction
    {
        private readonly ILogger<UsersSenderFunction> _logger;
        private readonly SGMembershipCalculator _calculator;

        public UsersSenderFunction(ILogger<UsersSenderFunction> logger, SGMembershipCalculator calculator)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _calculator = calculator;
        }

        [Function(nameof(UsersSenderFunction))]
        public async Task<string> SendUsersAsync([ActivityTrigger] UsersSenderRequest request)
        {
            string filePath = null;

            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object> { ["CurrentPart"] = request.CurrentPart, ["TotalParts"] = request.TotalParts }))
            {
                _logger.FunctionStarted(nameof(UsersSenderFunction));

                var users = JsonSerializer.Deserialize<List<AzureADUser>>(TextCompressor.Decompress(request.Users));
                filePath = await _calculator.SendMembershipAsync(request.SyncJob, users, request.CurrentPart, request.Exclusionary);

                _logger.UsersUploadedToBlob(users.Count, request.SyncJob.Query, request.GroupId);

                _logger.FunctionCompleted(nameof(UsersSenderFunction));

                return filePath;
            }
        }
    }
}