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
    public class GetTransitiveGroupCountFunction
    {
		private readonly ILoggingRepository _log;
		private readonly SGMembershipCalculator _calculator;

		public GetTransitiveGroupCountFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
		}

		[FunctionName(nameof(GetTransitiveGroupCountFunction))]
		public async Task<int> GetGroupsAsync([ActivityTrigger] GetTransitiveGroupCountRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(GetTransitiveGroupCountFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            _calculator.RunId = request.RunId;
            var response = await _calculator.GetGroupsCountAsync(request.GroupId);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(GetTransitiveGroupCountFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
