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
	public class RoomsReaderFunction
	{
		private readonly ILogger<RoomsReaderFunction> _logger;
		private readonly PlaceMembershipObtainerService _membershipProviderService;

		public RoomsReaderFunction(ILogger<RoomsReaderFunction> logger, PlaceMembershipObtainerService membershipProviderService)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _membershipProviderService = membershipProviderService ?? throw new ArgumentNullException(nameof(membershipProviderService));
		}

		[Function(nameof(RoomsReaderFunction))]
		public async Task<PlaceInformation> GetRoomsAsync([ActivityTrigger] RoomsReaderRequest request)
		{
			using (_logger.BeginRunIdScope(request.RunId))
			{
				_logger.FunctionStarted(nameof(RoomsReaderFunction));
				var response = await _membershipProviderService.GetRoomsAsync(request.Url, request.Top, request.Skip);
				_logger.FunctionCompleted(nameof(RoomsReaderFunction));
				return response;
			}
		}
	}
}