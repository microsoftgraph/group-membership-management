// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.DurableTask.Entities;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.Threading.Tasks;

namespace Hosts.AgentReader
{
    public sealed class AgentReaderStartEntity : TaskEntity<bool>
    {
        private readonly ILogger<AgentReaderStartEntity> _logger;

        public AgentReaderStartEntity(ILogger<AgentReaderStartEntity> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Start()
        {
            if (!Guid.TryParseExact(Context.Id.Key, "D", out var runId)
                || runId == Guid.Empty
                || Context.Id.Key != runId.ToString("D"))
            {
                throw new InvalidOperationException("AgentReader start entities require a canonical, non-empty RunId key.");
            }

            using var scope = _logger.BeginRunIdScope(runId);
            if (State)
            {
                _logger.DuplicateStartIgnored();
                return;
            }

            // The entity runtime commits the marker and this outgoing start action together.
            // A separate DurableTaskClient call here would not participate in that checkpoint.
            Context.ScheduleNewOrchestration(
                nameof(OrchestratorFunction),
                new AgentReaderRequest(runId),
                new StartOrchestrationOptions { InstanceId = AgentReaderInstance.GetId(runId) });
            State = true;
            _logger.InstanceScheduled();
        }

        [Function(nameof(AgentReaderStartEntity))]
        public static Task Run([EntityTrigger] TaskEntityDispatcher dispatcher) =>
            dispatcher.DispatchAsync<AgentReaderStartEntity>();
    }
}
