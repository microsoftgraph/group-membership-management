// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Graph;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;

namespace Hosts.GroupMembershipObtainer
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

		[Function(nameof(SubsequentMembersReaderFunction))]
		public async Task<GroupInformation> GetMembersAsync([ActivityTrigger] SubsequentMembersReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentMembersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			_calculator.RunId = request.RunId;
            var response = await _calculator.GetNextTransitiveMembersPageAsync(request.NextPageUrl);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentMembersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
