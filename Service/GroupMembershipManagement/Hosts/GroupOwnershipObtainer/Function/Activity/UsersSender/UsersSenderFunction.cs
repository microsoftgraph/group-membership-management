// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupOwnershipObtainer
{
    public class UsersSenderFunction
    {
        private readonly ILogger<UsersSenderFunction> _logger;
        private readonly IGroupOwnershipObtainerService _groupOwnershipObtainerService;

        public UsersSenderFunction(ILogger<UsersSenderFunction> logger, IGroupOwnershipObtainerService groupOwnershipObtainerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _groupOwnershipObtainerService = groupOwnershipObtainerService ?? throw new ArgumentNullException(nameof(groupOwnershipObtainerService));
        }

        [Function(nameof(UsersSenderFunction))]
        public async Task<string> SendUsersAsync([ActivityTrigger] UsersSenderRequest request)
        {
            using var scope = _logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            });
            _logger.FunctionStarted(nameof(UsersSenderFunction));
            var filePath = await _groupOwnershipObtainerService.SendMembershipAsync(request.SyncJob, request.GroupId, request.Users, request.CurrentPart, request.Exclusionary);
            _logger.UsersUploaded(request.Users.Count, request.SyncJob.Query, request.GroupId);
            _logger.FunctionCompleted(nameof(UsersSenderFunction));
            return filePath;
        }
    }
}