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
	public class WorkSpacesReaderFunction
	{
		private readonly ILogger<WorkSpacesReaderFunction> _logger;
		private readonly PlaceMembershipObtainerService _membershipProviderService;

		public WorkSpacesReaderFunction(ILogger<WorkSpacesReaderFunction> logger, PlaceMembershipObtainerService membershipProviderService)
		{
			_logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _membershipProviderService = membershipProviderService ?? throw new ArgumentNullException(nameof(membershipProviderService));
		}

		[Function(nameof(WorkSpacesReaderFunction))]
		public async Task<PlaceInformation> GetWorkSpacesAsync([ActivityTrigger] WorkSpacesReaderRequest request)
		{
			using (_logger.BeginRunIdScope(request.RunId))
			{
				_logger.FunctionStarted(nameof(WorkSpacesReaderFunction));
				var response = await _membershipProviderService.GetWorkSpacesAsync(request.Url, request.Top, request.Skip);
				_logger.FunctionCompleted(nameof(WorkSpacesReaderFunction));
				return response;
			}
		}
	}
}