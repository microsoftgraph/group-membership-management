// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure.AI.OpenAI;
using Azure.Identity;
using OpenAI.Chat;
using System.Diagnostics.CodeAnalysis;

namespace WebApi.BackgroundServices
{
    [ExcludeFromCodeCoverage]
    public class OpenAIService
    {
        private readonly string _endpoint;
        private readonly string _deploymentName = "gpt-4o";
        private readonly AzureOpenAIClient _openAIClient;
        private readonly ChatClient _chatClient;

        public OpenAIService(IConfiguration configuration)
        {
            _endpoint = configuration["Settings:OpenAIEndpoint"];

            if (string.IsNullOrWhiteSpace(_endpoint))
            {
                throw new ArgumentNullException(nameof(_endpoint), "OpenAI endpoint is not configured.");
            }

            _openAIClient = new AzureOpenAIClient(new Uri(_endpoint), new DefaultAzureCredential());
            _chatClient = _openAIClient.GetChatClient(_deploymentName);
        }

        public async Task<string> GetTitleAsync(string prompt)
        {
            var requestOptions = new ChatCompletionOptions()
            {
                MaxOutputTokenCount = 4096,
                Temperature = 1.0f,
                TopP = 1.0f,
            };

            List<ChatMessage> messages = new List<ChatMessage>()
            {
                new SystemChatMessage("You are a helpful assistant."),
                new UserChatMessage(prompt),
            };

            var response = await _chatClient.CompleteChatAsync(messages, requestOptions);
            return response.Value.Content[0].Text;
        }
    }
}
