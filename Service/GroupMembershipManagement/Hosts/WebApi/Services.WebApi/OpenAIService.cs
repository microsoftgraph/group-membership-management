// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using OpenAI.Chat;
using Services.WebApi.AI;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using Polly;
using Microsoft.Extensions.Configuration;
using Services.WebApi.Contracts;

namespace Services.WebApi
{
    [ExcludeFromCodeCoverage]
    public class OpenAIService : IOpenAIService
    {
        private readonly string _endpoint;
        private readonly ChatClient _chatClient;
        private readonly IAsyncPolicy _retryPolicy;

        public OpenAIService(IConfiguration configuration)
        {
            _endpoint = configuration["Settings:OpenAIEndpoint"];

            if (string.IsNullOrWhiteSpace(_endpoint))
            {
                throw new ArgumentNullException(nameof(_endpoint), "OpenAI endpoint is not configured.");
            }

            // Retire pooled connections after 1s so retries reconnect to a healthy node instead of staying pinned to a wedged one returning 403.
            var transportHandler = new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromSeconds(1)
            };
            _chatClient = AzureOpenAIChatClientBuilder.Create(
                _endpoint,
                new HttpClientPipelineTransport(new HttpClient(transportHandler)));

            _retryPolicy = Policy
                .Handle<ClientResultException>(ex =>
                    ex.Status == 429 ||
                    ex.Status == 500 ||
                    ex.Status == 502 ||
                    ex.Status == 503 ||
                    ex.Status == 504)
                .Or<TaskCanceledException>()
                .Or<HttpRequestException>()
                .Or<TimeoutException>()
                .Or<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 10)), // Cap at 10 seconds
                    onRetry: (exception, timespan, retryCount, context) => {});
        }

        public async Task<string> GetTitleAsync(string prompt)
        {
            var requestOptions = new ChatCompletionOptions()
            {
                Temperature = 0.3f,
                TopP = 0.8f,
                FrequencyPenalty = 0.0f,
                PresencePenalty = 0.0f
            };

            List<ChatMessage> messages = new List<ChatMessage>()
            {
                new SystemChatMessage("You are a concise assistant that generates short, descriptive titles for SQL filter conditions. Be direct and efficient."),
                new UserChatMessage(prompt),
            };

            var startTime = DateTime.UtcNow;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            try
            {
                var response = await _retryPolicy.ExecuteAsync(async () =>
                {
                    try
                    {
                        return await _chatClient.CompleteChatAsync(messages, requestOptions, timeoutCts.Token);
                    }
                    catch (ClientResultException ex) when (ex.Status == 429)
                    {
                        throw new Exception($"OpenAI API rate limited (HTTP 429). Will retry with exponential backoff. Error: {ex.Message}");
                    }
                });

                var duration = DateTime.UtcNow - startTime;
                return response.Value.Content[0].Text;
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                var duration = DateTime.UtcNow - startTime;
                throw new TimeoutException($"OpenAI API call timed out after {duration.TotalSeconds} seconds");
            }
        }

        public async Task<string> GetCompletionAsync(string systemPrompt, string userPrompt)
        {
            var requestOptions = new ChatCompletionOptions()
            {
                Temperature = 0.2f,
                TopP = 0.8f,
                FrequencyPenalty = 0.0f,
                PresencePenalty = 0.0f
            };

            List<ChatMessage> messages = new List<ChatMessage>()
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage(userPrompt),
            };

            var startTime = DateTime.UtcNow;
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            try
            {
                var response = await _retryPolicy.ExecuteAsync(async () =>
                {
                    try
                    {
                        return await _chatClient.CompleteChatAsync(messages, requestOptions, timeoutCts.Token);
                    }
                    catch (ClientResultException ex) when (ex.Status == 429)
                    {
                        throw new Exception($"OpenAI API rate limited (HTTP 429). Will retry with exponential backoff. Error: {ex.Message}");
                    }
                });

                var duration = DateTime.UtcNow - startTime;
                return response.Value.Content[0].Text;
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                var duration = DateTime.UtcNow - startTime;
                throw new TimeoutException($"OpenAI API call timed out after {duration.TotalSeconds} seconds");
            }
        }
    }
}
