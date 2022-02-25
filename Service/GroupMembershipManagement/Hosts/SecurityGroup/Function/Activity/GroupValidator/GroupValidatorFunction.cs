// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Polly;
using Repositories.Contracts;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
	public class GroupValidatorFunction
	{
		private readonly ILoggingRepository _log;
		private readonly SGMembershipCalculator _calculator;
		private const int NumberOfGraphRetries = 5;
		private const string SyncDisabledNoGroupEmailBody = "SyncDisabledNoGroupEmailBody";

		public GroupValidatorFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository;
			_calculator = calculator;
		}

		[FunctionName(nameof(GroupValidatorFunction))]
		public async Task<bool> ValidateGroupAsync([ActivityTrigger] GroupValidatorRequest request, ILogger log)
		{
			bool isExistingGroup = false;
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupValidatorFunction)} function started", RunId = request.RunId });
			var groupExistsResult = await _calculator.GroupExistsAsync(request.ObjectId, request.RunId);
			if (groupExistsResult.Outcome == OutcomeType.Successful && groupExistsResult.Result)
			{
				await _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Group with ID {request.ObjectId} exists." });
				isExistingGroup = true;
			}
			else
			{
				if (groupExistsResult.Outcome == OutcomeType.Successful)
                {
					await _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Group with ID {request.ObjectId} doesn't exist. Stopping sync and marking as error." });
					if (request.SyncJob != null && request.ObjectId != null) await _calculator.SendEmailAsync(request.SyncJob, request.RunId, SyncDisabledNoGroupEmailBody, new[] { request.ObjectId.ToString() });
				}
				else if (groupExistsResult.FaultType == FaultType.ExceptionHandledByThisPolicy)
					await _log.LogMessageAsync(new LogMessage { RunId = request.RunId, Message = $"Exceeded {NumberOfGraphRetries} while trying to determine if a group exists. Stopping sync and marking as error." });

				if (groupExistsResult.FinalException != null) { throw groupExistsResult.FinalException; }
				isExistingGroup = false;
			}
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(GroupValidatorFunction)} function completed", RunId = request.RunId });
			return isExistingGroup;
		}
	}
}