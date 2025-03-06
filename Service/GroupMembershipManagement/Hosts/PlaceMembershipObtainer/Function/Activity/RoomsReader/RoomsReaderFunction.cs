// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Models;
using Repositories.Contracts;
using Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.PlaceMembershipObtainer
{
	public class RoomsReaderFunction
	{
		private readonly ILoggingRepository _log;
		private readonly PlaceMembershipObtainerService _membershipProviderService;

		public RoomsReaderFunction(ILoggingRepository loggingRepository, PlaceMembershipObtainerService membershipProviderService)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _membershipProviderService = membershipProviderService ?? throw new ArgumentNullException(nameof(membershipProviderService));
		}

		[FunctionName(nameof(RoomsReaderFunction))]
		public async Task<PlaceInformation> GetRoomsAsync([ActivityTrigger] RoomsReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(RoomsReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			var response = await _membershipProviderService.GetRoomsAsync(request.Url, request.Top, request.Skip);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(RoomsReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
