// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Http.HttpClientLibrary.Middleware.Options;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Services
{
    // Agent ID APIs are only available on the Graph beta endpoint.
    public class DemoAgentGraphService : IDemoAgentGraphService
    {
        public const string BlueprintDisplayName = "GMM Demo Agent Blueprint";
        private const string BetaBaseUrl = "https://graph.microsoft.com/beta";

        private static readonly Dictionary<string, ParsableFactory<IParsable>> ErrorMapping = new()
        {
            { "XXX", ODataError.CreateFromDiscriminatorValue }
        };

        private readonly GraphServiceClient _graphClient;
        private readonly Func<CancellationToken, Task> _recoveryDelay;

        public DemoAgentGraphService(GraphServiceClient graphClient, Func<CancellationToken, Task> recoveryDelay = null)
        {
            _graphClient = graphClient ?? throw new ArgumentNullException(nameof(graphClient));
            _recoveryDelay = recoveryDelay ?? (token => Task.Delay(TimeSpan.FromSeconds(2), token));
        }

        public async Task<List<ExistingDemoAgent>> GetDemoAgentsAsync(CancellationToken cancellationToken = default)
        {
            var agents = new List<ExistingDemoAgent>();
            var url = $"{BetaBaseUrl}/users/microsoft.graph.agentUser?$select=id,userPrincipalName&$expand=manager($select=id)&$top=999";

            while (!string.IsNullOrEmpty(url))
            {
                using var document = await SendAsync(Method.GET, url, null, cancellationToken);
                foreach (var item in document.RootElement.GetProperty("value").EnumerateArray())
                {
                    var upn = GetString(item, "userPrincipalName");
                    if (upn == null || !upn.StartsWith(DemoAgentPlanner.UpnPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    agents.Add(new ExistingDemoAgent
                    {
                        Id = GetString(item, "id"),
                        UserPrincipalName = upn,
                        ManagerId = item.TryGetProperty("manager", out var manager) && manager.ValueKind == JsonValueKind.Object
                            ? GetString(manager, "id")
                            : null
                    });
                }

                url = GetString(document.RootElement, "@odata.nextLink");
                if (url != null && !url.StartsWith(BetaBaseUrl + "/", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unexpected Graph nextLink host.");
            }

            return agents;
        }

        public async Task<string> EnsureBlueprintAsync(CancellationToken cancellationToken = default, string sponsorId = null)
        {
            var filter = Uri.EscapeDataString($"displayName eq '{BlueprintDisplayName}'");
            string appId;
            using (var found = await SendAsync(Method.GET,
                $"{BetaBaseUrl}/applications/microsoft.graph.agentIdentityBlueprint?$filter={filter}&$select=id,appId", null, cancellationToken))
            {
                appId = found.RootElement.GetProperty("value").EnumerateArray().Select(x => GetString(x, "appId")).FirstOrDefault();
            }

            if (appId == null)
            {
                if (string.IsNullOrWhiteSpace(sponsorId))
                    throw new ArgumentException("Blueprint creation requires a sponsor.", nameof(sponsorId));
                var body = new JsonObject
                {
                    ["displayName"] = BlueprintDisplayName,
                    ["sponsors@odata.bind"] = new JsonArray($"{BetaBaseUrl}/users/{sponsorId}")
                };
                using var created = await SendAsync(Method.POST, $"{BetaBaseUrl}/applications/microsoft.graph.agentIdentityBlueprint", body, cancellationToken);
                appId = GetString(created.RootElement, "appId") ?? throw new InvalidDataException("Blueprint creation returned no appId.");
            }

            var spFilter = Uri.EscapeDataString($"appId eq '{appId}'");
            using var principal = await SendAsync(Method.GET, $"{BetaBaseUrl}/servicePrincipals/microsoft.graph.agentIdentityBlueprintPrincipal?$filter={spFilter}&$select=id", null, cancellationToken);
            if (principal.RootElement.GetProperty("value").GetArrayLength() == 0)
            {
                var body = new JsonObject
                {
                    ["@odata.type"] = "#microsoft.graph.agentIdentityBlueprintPrincipal",
                    ["appId"] = appId
                };
                using var _ = await SendAsync(Method.POST, $"{BetaBaseUrl}/servicePrincipals/microsoft.graph.agentIdentityBlueprintPrincipal", body, cancellationToken);
            }

            return appId;
        }

        public async Task<string> CreateAgentAsync(DemoAgentSpec spec, string blueprintAppId, CancellationToken cancellationToken = default)
        {
            if (spec == null || string.IsNullOrWhiteSpace(spec.DisplayName) ||
                string.IsNullOrWhiteSpace(spec.UserPrincipalName) || string.IsNullOrWhiteSpace(spec.ManagerId) ||
                string.IsNullOrWhiteSpace(blueprintAppId))
                throw new ArgumentException("Agent creation requires a name, UPN, sponsor and blueprint.");

            var identityId = await FindIdentityAsync(spec, blueprintAppId, cancellationToken);
            var existingUserId = await FindUserAsync(spec, identityId, cancellationToken);
            if (existingUserId != null)
                return existingUserId;

            var identityBody = new JsonObject
            {
                ["displayName"] = spec.DisplayName,
                ["agentIdentityBlueprintId"] = blueprintAppId,
                ["sponsors@odata.bind"] = new JsonArray($"{BetaBaseUrl}/users/{spec.ManagerId}")
            };
            if (identityId == null)
            {
                try
                {
                    using var identity = await SendAsync(Method.POST, $"{BetaBaseUrl}/servicePrincipals/microsoft.graph.agentIdentity", identityBody, cancellationToken);
                    identityId = GetString(identity.RootElement, "id") ?? throw new InvalidDataException("Agent identity creation returned no id.");
                }
                catch (Exception exception) when (IsAmbiguousWrite(exception, cancellationToken))
                {
                    identityId = await RecoverAsync(() => FindIdentityAsync(spec, blueprintAppId, cancellationToken), cancellationToken);
                    if (identityId == null)
                        throw;
                }
            }

            var userBody = new JsonObject
            {
                ["accountEnabled"] = true,
                ["displayName"] = spec.DisplayName,
                ["mailNickname"] = spec.MailNickname,
                ["userPrincipalName"] = spec.UserPrincipalName,
                ["identityParentId"] = identityId
            };
            try
            {
                using var user = await SendAsync(Method.POST, $"{BetaBaseUrl}/users/microsoft.graph.agentUser", userBody, cancellationToken);
                return GetString(user.RootElement, "id") ?? throw new InvalidDataException("Agent user creation returned no id.");
            }
            catch (Exception exception) when (IsAmbiguousWrite(exception, cancellationToken) ||
                exception is ApiException { ResponseStatusCode: 400 or 409 })
            {
                var recovered = await RecoverAsync(() => FindUserAsync(spec, identityId, cancellationToken), cancellationToken);
                if (recovered == null)
                    throw;
                return recovered;
            }
        }

        private static bool IsAmbiguousWrite(Exception exception, CancellationToken cancellationToken)
            => !cancellationToken.IsCancellationRequested &&
                (exception is HttpRequestException || exception is TaskCanceledException ||
                 exception is ApiException { ResponseStatusCode: >= 500 } ||
                 exception is ApiException { ResponseStatusCode: 408 });

        private async Task<string> RecoverAsync(Func<Task<string>> lookup, CancellationToken cancellationToken)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                await _recoveryDelay(cancellationToken);
                var id = await lookup();
                if (id != null)
                    return id;
            }
            return null;
        }

        private async Task<string> FindIdentityAsync(DemoAgentSpec spec, string blueprintAppId, CancellationToken cancellationToken)
        {
            // Filter only on the inherited displayName property; validate blueprint and sponsor locally.
            var filter = Uri.EscapeDataString($"displayName eq '{spec.DisplayName.Replace("'", "''")}'");
            var matches = await ReadCollectionAsync(
                $"{BetaBaseUrl}/servicePrincipals/microsoft.graph.agentIdentity?$filter={filter}&$select=id,displayName,agentIdentityBlueprintId&$expand=sponsors($select=id)",
                cancellationToken);
            if (matches.Count == 0)
                return null;
            if (matches.Count != 1 ||
                !string.Equals(GetString(matches[0], "displayName"), spec.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetString(matches[0], "agentIdentityBlueprintId"), blueprintAppId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Conflicting or duplicate demo agent identities; refusing to reuse or create another identity.");

            var id = GetString(matches[0], "id") ?? throw new InvalidDataException("Existing agent identity has no id.");
            if (matches[0].TryGetProperty("sponsors@odata.nextLink", out _) ||
                !matches[0].TryGetProperty("sponsors", out var sponsors) ||
                sponsors.ValueKind != JsonValueKind.Array || sponsors.GetArrayLength() != 1 ||
                !string.Equals(GetString(sponsors[0], "id"), spec.ManagerId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Existing agent identity does not have the expected sole sponsor.");
            return id;
        }

        private async Task<string> FindUserAsync(DemoAgentSpec spec, string identityId, CancellationToken cancellationToken)
        {
            JsonDocument user;
            try
            {
                user = await SendAsync(Method.GET,
                    $"{BetaBaseUrl}/users/{Uri.EscapeDataString(spec.UserPrincipalName)}?$select=id,userPrincipalName,identityParentId",
                    null, cancellationToken);
            }
            catch (ApiException exception) when (exception.ResponseStatusCode == 404)
            {
                return null;
            }
            using (user)
            {
                var item = user.RootElement;
                if (identityId == null || GetString(item, "@odata.type") != "#microsoft.graph.agentUser" ||
                    !string.Equals(GetString(item, "userPrincipalName"), spec.UserPrincipalName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(GetString(item, "identityParentId"), identityId, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The requested UPN belongs to an unrelated user or agent identity.");
                return GetString(item, "id") ?? throw new InvalidDataException("Existing agent user has no id.");
            }
        }

        private async Task<List<JsonElement>> ReadCollectionAsync(string url, CancellationToken cancellationToken)
        {
            var items = new List<JsonElement>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (url != null)
            {
                if (!url.StartsWith(BetaBaseUrl + "/", StringComparison.OrdinalIgnoreCase) || !visited.Add(url))
                    throw new InvalidDataException("Unexpected Graph nextLink.");
                using var document = await SendAsync(Method.GET, url, null, cancellationToken);
                items.AddRange(document.RootElement.GetProperty("value").EnumerateArray().Select(item => item.Clone()));
                url = GetString(document.RootElement, "@odata.nextLink");
            }
            return items;
        }

        public async Task SetManagerAsync(string agentId, string managerId, CancellationToken cancellationToken = default)
        {
            var body = new JsonObject { ["@odata.id"] = $"{BetaBaseUrl}/users/{managerId}" };
            var request = CreateRequest(Method.PUT, $"{BetaBaseUrl}/users/{agentId}/manager/$ref", body);
            await _graphClient.RequestAdapter.SendNoContentAsync(request, ErrorMapping, cancellationToken);
        }

        private async Task<JsonDocument> SendAsync(Method method, string url, JsonNode body, CancellationToken cancellationToken)
        {
            var request = CreateRequest(method, url, body);
            await using var stream = await _graphClient.RequestAdapter.SendPrimitiveAsync<Stream>(request, ErrorMapping, cancellationToken)
                ?? throw new InvalidDataException($"Graph returned no content for {method} {url}.");
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }

        private static RequestInformation CreateRequest(Method method, string url, JsonNode body)
        {
            var request = new RequestInformation { HttpMethod = method, URI = new Uri(url) };
            // Creation may commit before a transient response; readback, not middleware replay, must recover it.
            if (method == Method.POST)
                request.AddRequestOptions(new[] { new RetryHandlerOption { MaxRetry = 0 } });
            request.Headers.Add("Accept", "application/json");
            if (body != null)
                request.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body.ToJsonString())), "application/json");
            return request;
        }

        private static string GetString(JsonElement element, string name)
            => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
