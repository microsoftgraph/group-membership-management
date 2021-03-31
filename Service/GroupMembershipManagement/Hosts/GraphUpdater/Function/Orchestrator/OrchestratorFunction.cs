// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;

        public OrchestratorFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task<GroupMembershipMessageResponse> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context,
            ILogger log)
        {
            // Not allowed to await things that aren't another azure function
            _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(OrchestratorFunction) + " function started" }).ConfigureAwait(false);
            var graphRequest = context.GetInput<GraphUpdaterFunctionRequest>();

            var graphActivityResult = await context.CallActivityAsync<GroupMembershipMessageResponse>(nameof(GraphUpdaterFunction), graphRequest);

            _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = nameof(OrchestratorFunction) + " function completed" }).ConfigureAwait(false);

            return graphActivityResult;
        }
    }
}
