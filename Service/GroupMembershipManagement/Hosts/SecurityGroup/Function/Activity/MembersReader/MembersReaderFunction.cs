// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Graph;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
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
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
		}

		[FunctionName(nameof(MembersReaderFunction))]
		public async Task<GroupInformation> GetMembersAsync([ActivityTrigger] MembersReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(MembersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			_calculator.RunId = request.RunId;
            var response = await _calculator.GetFirstTransitiveMembersPageAsync(request.GroupId, request.RunId);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(MembersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
