// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Models;
using Repositories.Contracts.Helpers;
using Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.PlaceMembershipObtainer
{
	public class UsersReaderFunction
	{
		private readonly ILogger<UsersReaderFunction> _logger;
		private readonly PlaceMembershipObtainerService _membershipProviderService;

		public UsersReaderFunction(ILogger<UsersReaderFunction> logger, PlaceMembershipObtainerService membershipProviderService)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _membershipProviderService = membershipProviderService ?? throw new ArgumentNullException(nameof(membershipProviderService));
		}

		[Function(nameof(UsersReaderFunction))]
		public async Task<UserInformation> GetUsersAsync([ActivityTrigger] UsersReaderRequest request)
		{
			using (_logger.BeginRunIdScope(request.RunId))
			{
				_logger.FunctionStarted(nameof(UsersReaderFunction));
				var response = await _membershipProviderService.GetUsersAsync(request.Url);
				_logger.FunctionCompleted(nameof(UsersReaderFunction));
				return response;
			}
		}
	}
}