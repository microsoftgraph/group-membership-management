// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services;
using System.Threading.Tasks;

namespace Hosts.PlaceMembershipObtainer
{
    public class UsersSenderFunction
    {
        private readonly ILogger<UsersSenderFunction> _logger;
        private readonly PlaceMembershipObtainerService _membershipProviderService;

        public UsersSenderFunction(ILogger<UsersSenderFunction> logger, PlaceMembershipObtainerService membershipProviderService)
        {
            _logger = logger;
            _membershipProviderService = membershipProviderService;
        }

        [Function(nameof(UsersSenderFunction))]
        public async Task<string> SendUsersAsync([ActivityTrigger] UsersSenderRequest request)
        {
            string filePath = null;

            using (_logger.BeginRunIdScope(request.RunId))
            {
                _logger.FunctionStarted(nameof(UsersSenderFunction));

                filePath = await _membershipProviderService.SendMembershipAsync(request.SyncJob, request.GroupId, request.Users, request.CurrentPart, request.Exclusionary);

                _logger.SuccessfullyUploadedUsers(request.Users.Count, request.SyncJob.Query, request.GroupId);

                _logger.FunctionCompleted(nameof(UsersSenderFunction));
            }

            return filePath;
        }
    }
}