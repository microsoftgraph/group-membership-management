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
    public class SubsequentUsersReaderFunction
    {
		private readonly ILoggingRepository _log;
		private readonly SGMembershipCalculator _calculator;

		public SubsequentUsersReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
		}

		[FunctionName(nameof(SubsequentUsersReaderFunction))]
		public async Task<DeltaGroupInformation> GetUsersAsync([ActivityTrigger] SubsequentUsersReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentUsersReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			_calculator.RunId = request.RunId;
            var response = await _calculator.GetNextUsersPageAsync(request.NextPageUrl);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SubsequentUsersReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
