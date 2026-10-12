// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class DemoAgentWriterFunction
    {
        private const int ManagerAttempts = 3;

        private readonly IDemoAgentGraphService _graphService;
        private readonly ILogger<DemoAgentWriterFunction> _logger;
        public DemoAgentWriterFunction(IDemoAgentGraphService graphService, ILogger<DemoAgentWriterFunction> logger)
        {
            _graphService = graphService ?? throw new ArgumentNullException(nameof(graphService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Func<TimeSpan, Task> Delay { get; set; } = Task.Delay;

        [Function(nameof(DemoAgentWriterFunction))]
        public async Task<DemoAgentWriterResult> RunAsync([ActivityTrigger] DemoAgentWriterRequest request)
        {
            var result = new DemoAgentWriterResult();

            foreach (var spec in request.ToCreate ?? new List<DemoAgentSpec>())
            {
                string agentId;
                try
                {
                    agentId = await _graphService.CreateAgentAsync(spec, request.BlueprintAppId);
                    result.Created++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    _logger.DemoAgentCreateFailed(spec.UserPrincipalName, ex);
                    continue;
                }

                // A new agent user may not be replicated yet, so retry the manager assignment.
                if (!await TrySetManagerAsync(agentId, spec.ManagerId))
                    result.Failed++;
            }

            foreach (var assignment in request.ToFixManager ?? new List<ManagerAssignment>())
            {
                if (await TrySetManagerAsync(assignment.AgentId, assignment.ManagerId))
                    result.ManagersFixed++;
                else
                    result.Failed++;
            }

            return result;
        }

        private async Task<bool> TrySetManagerAsync(string agentId, string managerId)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await _graphService.SetManagerAsync(agentId, managerId);
                    return true;
                }
                catch (Exception ex) when (attempt < ManagerAttempts)
                {
                    _logger.DemoAgentManagerRetry(agentId, attempt, ex.Message);
                    await Delay(TimeSpan.FromSeconds(5 * attempt));
                }
                catch (Exception ex)
                {
                    _logger.DemoAgentManagerFailed(agentId, ex);
                    return false;
                }
            }
        }
    }
}
