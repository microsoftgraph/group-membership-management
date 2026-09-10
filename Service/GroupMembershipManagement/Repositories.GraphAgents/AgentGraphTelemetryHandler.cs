// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.GraphAgents
{
    public sealed class AgentGraphTelemetryHandler : DelegatingHandler
    {
        private static readonly HttpRequestOptionsKey<AgentGraphRequestContext> ContextKey =
            new(typeof(AgentGraphRequestContext).FullName!);
        private readonly ILogger<AgentGraphTelemetryHandler> _logger;
        private readonly TelemetryClient _telemetryClient;

        public AgentGraphTelemetryHandler(ILogger<AgentGraphTelemetryHandler> logger, TelemetryClient telemetryClient)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.Options.TryGetValue(ContextKey, out var context) || context.RunId == Guid.Empty)
            {
                throw new InvalidOperationException("An AgentReader Graph request requires a RunId.");
            }

            var response = await base.SendAsync(request, cancellationToken);
            await GraphTelemetryHelper.TrackResourceUnitsAsync(
                response, QueryType.AgentUser, context.RunId, _logger, _telemetryClient,
                GraphOperationType.Read, "Source");
            return response;
        }
    }
}
