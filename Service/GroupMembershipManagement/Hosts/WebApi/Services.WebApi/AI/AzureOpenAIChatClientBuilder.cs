// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.Core;
using Azure.Identity;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace Services.WebApi.AI
{
    internal static class AzureOpenAIChatClientBuilder
    {
        internal const string DeploymentName = "gpt-5.4-mini";
        private const string AuthenticationScope = "https://ai.azure.com/.default";

        internal static ChatClient Create(
            string endpoint,
            PipelineTransport? transport = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

            var options = new OpenAIClientOptions
            {
                Endpoint = NormalizeEndpoint(endpoint),
            };
            if (transport is not null)
            {
                options.Transport = transport;
            }

            var credential = new DefaultAzureCredential(
                DefaultAzureCredential.DefaultEnvironmentVariableName);
            var tokenProvider = new AzureCredentialTokenProvider(
                credential,
                AuthenticationScope);
#pragma warning disable OPENAI001
            var authenticationPolicy = new BearerTokenPolicy(
                tokenProvider,
                AuthenticationScope);

            var client = new ChatClient(
                DeploymentName,
                authenticationPolicy,
                options);
#pragma warning restore OPENAI001
            return client;
        }

        internal static Uri NormalizeEndpoint(string endpoint)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
            var builder = new UriBuilder(endpoint)
            {
                Query = string.Empty,
                Fragment = string.Empty,
            };
            var path = builder.Path.TrimEnd('/');
            if (!path.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
            {
                path = $"{path}/openai/v1";
            }

            builder.Path = $"{path.TrimStart('/')}/";
            return builder.Uri;
        }

        private sealed class AzureCredentialTokenProvider(
            TokenCredential credential,
            string scope) : AuthenticationTokenProvider
        {
            public override GetTokenOptions CreateTokenOptions(
                IReadOnlyDictionary<string, object> properties) =>
                new(properties);

            public override AuthenticationToken GetToken(
                GetTokenOptions options,
                CancellationToken cancellationToken)
            {
                var token = credential.GetToken(
                    new TokenRequestContext([scope]),
                    cancellationToken);
                return ToAuthenticationToken(token);
            }

            public override async ValueTask<AuthenticationToken> GetTokenAsync(
                GetTokenOptions options,
                CancellationToken cancellationToken)
            {
                var token = await credential.GetTokenAsync(
                    new TokenRequestContext([scope]),
                    cancellationToken).ConfigureAwait(false);
                return ToAuthenticationToken(token);
            }

            private static AuthenticationToken ToAuthenticationToken(AccessToken token) =>
                new(token.Token, "Bearer", token.ExpiresOn, token.RefreshOn);
        }
    }
}
