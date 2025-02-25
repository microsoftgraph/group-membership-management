// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class SubsequentDeltaUserReaderFunction
    {
		private readonly ILoggingRepository _log;
		private readonly SGMembershipCalculator _calculator;

		public SubsequentDeltaUserReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
		}

		[FunctionName(nameof(SubsequentDeltaUserReaderFunction))]
		public async Task<DeltaGroupInformation> GetSubsequentDeltaUsersAsync([ActivityTrigger] SubsequentDeltaUserReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentDeltaUserReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			_calculator.RunId = request.RunId;
            var response = await _calculator.GetNextDeltaUsersPagesAsync(request.NextPageUrl, request.PageCount);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentDeltaUserReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
