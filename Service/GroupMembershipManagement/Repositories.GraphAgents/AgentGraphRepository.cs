// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.GraphAgents
{
    public sealed class AgentGraphRepository : IAgentGraphRepository
    {
        private const string AgentPath = "/v1.0/users/microsoft.graph.agentUser";
        private readonly GraphServiceClient _graphClient;
        private readonly IRetryPolicyProvider _retryPolicyProvider;
        private readonly ILogger<AgentGraphRepository> _logger;

        public AgentGraphRepository(
            GraphServiceClient graphClient,
            IRetryPolicyProvider retryPolicyProvider,
            ILogger<AgentGraphRepository> logger)
        {
            _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
            _retryPolicyProvider = retryPolicyProvider ?? throw new ArgumentNullException(nameof(retryPolicyProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AgentSourcePage> ReadPageAsync(Guid runId, string? nextLink, CancellationToken cancellationToken)
        {
            if (runId == Guid.Empty)
            {
                throw new ArgumentException("A non-empty RunId is required.", nameof(runId));
            }

            using var scope = _logger.BeginRunIdScope(runId);
            if (!Uri.TryCreate(_graphClient.RequestAdapter.BaseUrl, UriKind.Absolute, out var baseUri)
                || baseUri.Scheme != Uri.UriSchemeHttps || baseUri.AbsolutePath.TrimEnd('/') != "/v1.0")
            {
                throw new InvalidOperationException("AgentReader requires a Microsoft Graph v1.0 HTTPS client.");
            }

            var url = nextLink ?? baseUri.GetLeftPart(UriPartial.Authority) + AgentPath
                + "?$select=id,accountEnabled,agentIdentityBlueprintId"
                + "&$expand=manager($select=onPremisesImmutableId)";
            ValidatePageUrl(url, baseUri);

            var policy = _retryPolicyProvider.CreateRetryAfterPolicy(runId)
                .WrapAsync(_retryPolicyProvider.CreateExceptionHandlingPolicy(runId));
            HttpResponseMessage? response = null;
            try
            {
                response = await policy.ExecuteAsync(async token =>
                {
                    response?.Dispose();
                    response = null;
                    var nativeHandler = new NativeResponseHandler();
                    await _graphClient.Users.WithUrl(url).GetAsync(configuration =>
                    {
                        configuration.Options.Add(new AgentGraphRequestContext(runId));
                        configuration.Options.Add(new ResponseHandlerOption { ResponseHandler = nativeHandler });
                        configuration.Options.Add(new RetryHandlerOption { MaxRetry = 0 });
                        configuration.Options.Add(new RedirectHandlerOption { MaxRedirect = 0 });
                    }, token);

                    response = nativeHandler.Value as HttpResponseMessage
                        ?? throw new InvalidDataException("Graph did not return an HTTP response.");
                    return response;
                }, cancellationToken);

                response.EnsureSuccessStatusCode();
                using var buffer = new MemoryStream();
                using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                {
                    await stream.CopyToAsync(buffer, cancellationToken);
                }

                buffer.Position = 0;
                // Kiota coerces GUID/date-shaped additional strings; retain their original JSON values.
                using var document = JsonDocument.Parse(buffer);
                var elements = GetCollectionElements(document.RootElement);
                buffer.Position = 0;
                var parseNode = await ParseNodeFactoryRegistry.DefaultInstance.GetRootParseNodeAsync(
                    "application/json", buffer, cancellationToken);
                var page = parseNode.GetObjectValue(UserCollectionResponse.CreateFromDiscriminatorValue);
                if (page?.Value == null)
                {
                    throw new InvalidDataException("Graph did not return a user collection.");
                }

                // An entry the SDK cannot represent is silently dropped, which would shorten the table.
                if (page.Value.Count != elements.GetArrayLength())
                {
                    throw new InvalidDataException("Graph returned agent entries that could not be read.");
                }

                if (page.OdataNextLink != null)
                {
                    ValidatePageUrl(page.OdataNextLink, baseUri);
                }

                var agents = new List<AgentSourceRecord>(page.Value.Count);
                var index = 0;
                foreach (var element in elements.EnumerateArray())
                {
                    var agent = page.Value[index++];
                    if (agent == null)
                    {
                        throw new InvalidDataException("Graph returned a null agent entry.");
                    }

                    var manager = agent.Manager;
                    element.TryGetProperty("manager", out var managerElement);
                    var identifier = (manager as User)?.OnPremisesImmutableId
                        ?? GetAdditionalString(managerElement, "onPremisesImmutableId");
                    agents.Add(new AgentSourceRecord(
                        agent.Id, manager != null, identifier,
                        GetAdditionalString(element, "agentIdentityBlueprintId"), agent.AccountEnabled));
                }

                return new AgentSourcePage(agents, page.OdataNextLink);
            }
            finally
            {
                response?.Dispose();
            }
        }

        private static string? GetAdditionalString(JsonElement element, string property)
        {
            if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                || !element.TryGetProperty(property, out var value)
                || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : throw new InvalidDataException($"Graph returned a non-string {property}.");
        }

        private static JsonElement GetCollectionElements(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("value", out var value)
                || value.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("Graph did not return a user collection.");
            }

            foreach (var element in value.EnumerateArray())
            {
                // A null entry is malformed input, not an agent with a missing identifier.
                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("Graph returned a malformed agent entry.");
                }
            }

            return value;
        }

        private static void ValidatePageUrl(string url, Uri baseUri)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps
                || !string.Equals(uri.Authority, baseUri.Authority, StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath != AgentPath
                || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new InvalidDataException("Graph returned an invalid agent enumeration URL.");
            }
        }
    }
}
