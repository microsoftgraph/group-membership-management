// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
	public class SubsequentMembersReaderFunction
	{
		private readonly ILoggingRepository _log;
		private readonly SGMembershipCalculator _calculator;

		public SubsequentMembersReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
		}

		[FunctionName(nameof(SubsequentMembersReaderFunction))]
		public async Task<GroupInformation> GetMembersAsync([ActivityTrigger] SubsequentMembersReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentMembersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			var response = await _calculator.GetNextTransitiveMembersPageAsync(request.NextPageUrl, request.GroupMembersPage);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentMembersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
