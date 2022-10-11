// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Repositories.Contracts;
using Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.AzureMembershipProvider
{
	public class WorkSpacesReaderFunction
	{
		private readonly ILoggingRepository _log;
		private readonly AzureMembershipProviderService _membershipProviderService;

		public WorkSpacesReaderFunction(ILoggingRepository loggingRepository, AzureMembershipProviderService membershipProviderService)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _membershipProviderService = membershipProviderService ?? throw new ArgumentNullException(nameof(membershipProviderService));
		}

		[FunctionName(nameof(WorkSpacesReaderFunction))]
		public async Task<PlaceInformation> GetWorkSpacesAsync([ActivityTrigger] WorkSpacesReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(WorkSpacesReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			var response = await _membershipProviderService.GetWorkSpacesAsync(request.Url, request.Top, request.Skip);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(WorkSpacesReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
