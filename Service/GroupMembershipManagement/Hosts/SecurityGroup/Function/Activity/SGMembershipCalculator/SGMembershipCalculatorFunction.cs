// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
	public class SGMembershipCalculatorFunction
    {
		private readonly ILoggingRepository _loggingRepository;
		private readonly SGMembershipCalculator _calculator;

		public SGMembershipCalculatorFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
		{
			_loggingRepository = loggingRepository;
			_calculator = calculator;
		}

		[FunctionName(nameof(SGMembershipCalculatorFunction))]
        public async Task CalculateSGMembership([ActivityTrigger] SyncJob syncJob, ILogger log)
        {
			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SGMembershipCalculatorFunction)} function started", RunId = syncJob.RunId });
			await _calculator.SendMembershipAsync(syncJob);
			await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(SGMembershipCalculatorFunction)} function completed", RunId = syncJob.RunId });
					}
    }
}