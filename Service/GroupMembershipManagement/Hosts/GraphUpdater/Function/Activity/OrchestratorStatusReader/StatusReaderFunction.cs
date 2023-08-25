// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Threading.Tasks;

namespace Hosts.GraphUpdater
{
    public class StatusReaderFunction
    {
        [FunctionName(nameof(StatusReaderFunction))]
        public async Task<DurableOrchestrationStatus> GetStatusAsync(
            [ActivityTrigger] string orchestratorId, [DurableClient] IDurableOrchestrationClient client)
        {
            return await client.GetStatusAsync(orchestratorId);
        }
    }
}
