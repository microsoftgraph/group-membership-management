// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

namespace Services.WebApi.AI
{
    /// <summary>
    /// Creates the configured Azure OpenAI chat client.
    /// </summary>
    internal sealed class AzureOpenAIChatClientFactory : IChatClientFactory
    {
        private const string DeploymentName = "gpt-5.4-mini";
        private readonly ChatClient _chatClient;

        public AzureOpenAIChatClientFactory(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            var endpoint = configuration["Settings:OpenAIEndpoint"];
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                throw new ArgumentException(
                    "OpenAI endpoint is not configured (Settings:OpenAIEndpoint).",
                    nameof(configuration));
            }

            var credential = new DefaultAzureCredential(DefaultAzureCredential.DefaultEnvironmentVariableName);
            var openAIClient = new AzureOpenAIClient(new Uri(endpoint), credential);
            _chatClient = openAIClient.GetChatClient(DeploymentName);
        }

        public ChatClient CreateChatClient() => _chatClient;
    }
}
