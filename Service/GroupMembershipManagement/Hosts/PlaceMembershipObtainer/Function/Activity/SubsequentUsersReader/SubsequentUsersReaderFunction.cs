// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using Services;
using System;
using System.Threading.Tasks;

namespace Hosts.PlaceMembershipObtainer
{
    public class SubsequentUsersReaderFunction
	{
		private readonly ILogger<SubsequentUsersReaderFunction> _logger;
        private readonly PlaceMembershipObtainerService _membershipProviderService;

        public SubsequentUsersReaderFunction(ILogger<SubsequentUsersReaderFunction> logger, PlaceMembershipObtainerService membershipProviderService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _membershipProviderService = membershipProviderService ?? throw new ArgumentNullException(nameof(membershipProviderService));
        }

        [Function(nameof(SubsequentUsersReaderFunction))]
		public async Task<UserInformation> GetUsersAsync([ActivityTrigger] SubsequentUsersReaderRequest request)
		{
			using (_logger.BeginRunIdScope(request.RunId))
			{
				_logger.FunctionStarted(nameof(SubsequentUsersReaderFunction));
				var response = await _membershipProviderService.GetNextUsersAsync(request.NextPageUrl);
				_logger.FunctionCompleted(nameof(SubsequentUsersReaderFunction));
				return response;
			}
		}
	}
}