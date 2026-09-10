// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.DurableTask.Client;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.AgentReader
{
    public sealed class StarterFunction
    {
        private static readonly TimeSpan StartConfirmationTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan StartConfirmationPollInterval = TimeSpan.FromMilliseconds(250);

        private readonly ILogger<StarterFunction> _logger;
        private readonly TimeProvider _timeProvider;

        public StarterFunction(ILogger<StarterFunction> logger, TimeProvider timeProvider)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        [Function(nameof(StarterFunction))]
        public async Task<HttpResponseData> RunAsync(
            [HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestData req,
            [DurableClient] DurableTaskClient starter,
            CancellationToken cancellationToken = default)
        {
            var runId = await ReadRunIdAsync(req, cancellationToken);
            if (runId == null)
            {
                return req.CreateResponse(HttpStatusCode.BadRequest);
            }

            // Opened before the first run-scoped entry so every entry that pertains to a run carries
            // its identifier (FR-018). A rejected request has no run to trace.
            using var scope = _logger.BeginRunIdScope(runId.Value);
            _logger.FunctionStarted(nameof(StarterFunction));

            var instanceId = AgentReaderInstance.GetId(runId.Value);
            using var timeout = new CancellationTokenSource(StartConfirmationTimeout, _timeProvider);
            using var confirmation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

            try
            {
                var existing = await starter.GetInstanceAsync(instanceId, cancellation: confirmation.Token);
                if (existing == null)
                {
                    await starter.Entities.SignalEntityAsync(
                        AgentReaderInstance.GetStartEntityId(runId.Value),
                        nameof(AgentReaderStartEntity.Start),
                        cancellation: confirmation.Token);
                    _logger.StartRequested();

                    // Signaling only confirms enqueueing. ADF must not receive a polling URI whose
                    // instance is still absent, including when a retained start marker outlives history.
                    do
                    {
                        existing = await starter.GetInstanceAsync(instanceId, cancellation: confirmation.Token);
                        if (existing == null)
                        {
                            await Task.Delay(StartConfirmationPollInterval, _timeProvider, confirmation.Token);
                        }
                    }
                    while (existing == null);
                }
                else
                {
                    _logger.ExistingInstance(existing.RuntimeStatus.ToString());
                }
            }
            catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.StartNotConfirmed(exception, StartConfirmationTimeout.TotalSeconds);
                var unavailable = req.CreateResponse(HttpStatusCode.ServiceUnavailable);
                await unavailable.WriteAsJsonAsync(new
                {
                    errorCode = "AgentReaderStartNotConfirmed",
                    message = "The AgentReader instance could not be confirmed. Inspect this run before starting a new pipeline run.",
                    instanceId
                }, cancellationToken);
                return unavailable;
            }

            var response = await starter.CreateCheckStatusResponseAsync(req, instanceId, cancellation: cancellationToken);

            _logger.FunctionCompleted(nameof(StarterFunction));

            return response;
        }

        private async Task<Guid?> ReadRunIdAsync(HttpRequestData req, CancellationToken cancellationToken)
        {
            string body;
            using (var reader = new StreamReader(req.Body))
            {
                body = await reader.ReadToEndAsync(cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                _logger.InvalidRequest("the request body was empty");
                return null;
            }

            JsonElement payload;
            try
            {
                payload = JsonSerializer.Deserialize<JsonElement>(body);
            }
            catch (JsonException)
            {
                _logger.InvalidRequest("the request body was not valid JSON");
                return null;
            }

            if (payload.ValueKind != JsonValueKind.Object
                || !payload.TryGetProperty("RunId", out var element)
                || element.ValueKind != JsonValueKind.String)
            {
                _logger.InvalidRequest("the request did not carry a RunId");
                return null;
            }

            // A defaulted identifier would populate the wrong run's table, so the value is rejected
            // rather than substituted.
            if (!Guid.TryParse(element.GetString(), out var runId) || runId == Guid.Empty)
            {
                _logger.InvalidRequest("the RunId was absent, empty, or unparseable");
                return null;
            }

            return runId;
        }
    }
}
