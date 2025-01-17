// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GroupMembershipObtainer
{
    public class DeltaUserReaderFunction
    {
		private readonly ILoggingRepository _log;
		private readonly SGMembershipCalculator _calculator;

		public DeltaUserReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
			_calculator = calculator ?? throw new ArgumentNullException(nameof(calculator));
		}

		[FunctionName(nameof(DeltaUserReaderFunction))]
		public async Task<DeltaGroupInformation> GetDeltaUsersAsync([ActivityTrigger] DeltaUserReaderRequest request)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaUserReaderFunction)} function started", RunId = request.RunId }, VerbosityLevel.DEBUG);
			_calculator.RunId = request.RunId;
            var response = await _calculator.GetFirstDeltaUsersPageAsync(request.ObjectId, request.RunId);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(DeltaUserReaderFunction)} function completed", RunId = request.RunId }, VerbosityLevel.DEBUG);
			return response;
		}
	}
}
