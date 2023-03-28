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

namespace Hosts.AzureMembershipProvider
{
	public class UsersReaderFunction
	{
		private readonly ILoggingRepository _log;
		private readonly AzureMembershipProviderService _membershipProviderService;

		public UsersReaderFunction(ILoggingRepository loggingRepository, AzureMembershipProviderService membershipProviderService)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _membershipProviderService = membershipProviderService ?? throw new ArgumentNullException(nameof(membershipProviderService));
		}

		[FunctionName(nameof(UsersReaderFunction))]
		public async Task<UserInformation> GetUsersAsync([ActivityTrigger] UsersReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(UsersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			var response = await _membershipProviderService.GetUsersAsync(request.Url);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(UsersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
