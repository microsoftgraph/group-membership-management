// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.GroupMembershipObtainer
{
    public class GetUserCountFunction
    {
		private readonly ILoggingRepository _log;
		private readonly SGMembershipCalculator _calculator;

		public GetUserCountFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
		}

		[Function(nameof(GetUserCountFunction))]
		public async Task<int> GetUserCountAsync([ActivityTrigger] GetUserCountRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(GetUserCountFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
            _calculator.RunId = request.RunId;
            var response = await _calculator.GetUsersCountAsync(request.GroupId);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(GetUserCountFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
