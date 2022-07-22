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
	public class MembersReaderFunction
	{
		private readonly ILoggingRepository _log;
		private readonly SGMembershipCalculator _calculator;

		public MembersReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository;
			_calculator = calculator;
		}

		[FunctionName(nameof(MembersReaderFunction))]
		public async Task<(List<AzureADUser> users, Dictionary<string, int> nonUserGraphObjects, string nextPageUrl, IGroupTransitiveMembersCollectionWithReferencesPage usersFromGroup)> GetMembersAsync([ActivityTrigger] MembersReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(MembersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			var response = await _calculator.GetFirstTransitiveMembersPageAsync(request.ObjectId, request.RunId);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(MembersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
