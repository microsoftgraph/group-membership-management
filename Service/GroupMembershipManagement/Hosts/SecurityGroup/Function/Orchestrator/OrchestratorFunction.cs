// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using System.Threading.Tasks;

namespace Hosts.SecurityGroup
{
    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly SGMembershipCalculator _calculator;
        public OrchestratorFunction(ILoggingRepository loggingRepository, SGMembershipCalculator calculator)
        {
            _loggingRepository = loggingRepository;
            _calculator = calculator;
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            var syncJob = context.GetInput<SyncJob>();
            _loggingRepository.SyncJobProperties = syncJob.ToDictionary();
            if (!context.IsReplaying) _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function started", RunId = syncJob.RunId });
            await context.CallActivityAsync(nameof(SGMembershipCalculatorFunction), syncJob);
            if (!context.IsReplaying) _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function completed", RunId = syncJob.RunId });
        }
    }
}