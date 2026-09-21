// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Services.WebApi.AI;
using Services.WebApi.Contracts;

namespace Services.WebApi.DependencyInjection
{
    /// <summary>
    /// Registers the Copilot service graph.
    /// </summary>
    public static class CopilotServiceCollectionExtensions
    {
        /// <summary>
        /// Adds Copilot services when an OpenAI endpoint is configured.
        /// </summary>
        public static IServiceCollection AddCopilotServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(configuration);

            var openAIEndpoint = configuration["Settings:OpenAIEndpoint"];

            // Keep the fallback available when Copilot is not configured.
            services.AddScoped<IFeedbackRefinementService, UnavailableFeedbackRefinementService>();

            if (!string.IsNullOrWhiteSpace(openAIEndpoint))
            {
                services.AddSingleton<IChatClientFactory, AzureOpenAIChatClientFactory>();

                services.AddSingleton<IOpenAIService, OpenAIService>();
                services.AddScoped<ICopilotService, CopilotService>();
                services.AddScoped<IFeedbackRefinementService, FeedbackRefinementService>();
            }

            return services;
        }
    }
}
