// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Repositories.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class UsersReaderFunction
    {
		private readonly ILoggingRepository _log;
		private readonly SGMembershipCalculator _calculator;

		public UsersReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository;
			_calculator = calculator;
		}

		[FunctionName(nameof(UsersReaderFunction))]
		public async Task<(List<AzureADUser> users, string nextPageUrl, string deltaUrl, IGroupDeltaCollectionPage usersFromGroup)> GetUsersAsync([ActivityTrigger] UsersReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(UsersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			var response = await _calculator.GetFirstUsersPageAsync(request.ObjectId, request.RunId);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(UsersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
