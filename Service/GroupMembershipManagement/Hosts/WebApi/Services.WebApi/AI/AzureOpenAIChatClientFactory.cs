// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Configuration;
using OpenAI.Chat;

namespace Services.WebApi.AI
{
    /// <summary>
    /// Creates the configured Azure OpenAI chat client.
    /// </summary>
    internal sealed class AzureOpenAIChatClientFactory : IChatClientFactory
    {
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

            _chatClient = AzureOpenAIChatClientBuilder.Create(endpoint);
        }

        public ChatClient CreateChatClient() => _chatClient;
    }
}
