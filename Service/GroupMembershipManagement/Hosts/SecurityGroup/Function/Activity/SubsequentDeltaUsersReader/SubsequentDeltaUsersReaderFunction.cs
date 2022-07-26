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
    public class SubsequentDeltaUsersReaderFunction
    {
		private readonly ILoggingRepository _log;
		private readonly SGMembershipCalculator _calculator;

		public SubsequentDeltaUsersReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
		}

		[FunctionName(nameof(SubsequentDeltaUsersReaderFunction))]
		public async Task<DeltaGroupInformation> GetDeltaUsersAsync([ActivityTrigger] SubsequentDeltaUsersReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentDeltaUsersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			var response = await _calculator.GetNextDeltaUsersPageAsync(request.NextPageUrl, request.GroupUsersPage);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentDeltaUsersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
