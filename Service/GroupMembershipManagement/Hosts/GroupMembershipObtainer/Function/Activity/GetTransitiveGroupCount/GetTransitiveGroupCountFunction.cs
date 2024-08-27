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
    public class GetTransitiveGroupCountFunction
    {
		private readonly ILoggingRepository _log;
		private readonly SGMembershipCalculator _calculator;

		public GetTransitiveGroupCountFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
		}

		[Function(nameof(GetTransitiveGroupCountFunction))]
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
