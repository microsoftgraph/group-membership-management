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
    public class SourceGroupsReaderFunction
    {
		private readonly ILoggingRepository _log;
		private readonly SGMembershipCalculator _calculator;

		public SourceGroupsReaderFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
		{
			_log = loggingRepository;
			_calculator = calculator;
		}

		[FunctionName(nameof(SourceGroupsReaderFunction))]
		public async Task<AzureADGroup[]> GetSourceGroups([ActivityTrigger] SourceGroupsReaderRequest request, ILogger log)
		{
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SourceGroupsReaderFunction)} function started", RunId = request.RunId });
			await _log.LogMessageAsync(new LogMessage
			{
				RunId = request.RunId,
				Message = $"Reading source groups {request.SyncJob.Query} to be synced into the destination group {request.SyncJob.TargetOfficeGroupId}."
			});
			var sourceGroups = _calculator.ReadSourceGroups(request.SyncJob);
			await _log.LogMessageAsync(new LogMessage { Message = $"{nameof(SourceGroupsReaderFunction)} function completed", RunId = request.RunId });
			return sourceGroups;
		}
	}
}
