// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Services.WebApi;
using Services.WebApi.AI;
using Services.WebApi.Contracts;
using Services.WebApi.DependencyInjection;

namespace WebApi.Tests.DependencyInjection
{
    [TestClass]
    [ExcludeFromCodeCoverage]
    public class CopilotServiceCollectionExtensionsTests
    {
        private const string TestEndpoint = "https://test-openai-endpoint.example.com";

        private static IConfiguration BuildConfiguration(string? endpoint)
        {
            var mock = new Mock<IConfiguration>();
            mock.Setup(c => c["Settings:OpenAIEndpoint"]).Returns(endpoint!);
            return mock.Object;
        }

        [TestMethod]
        public void AddCopilotServices_EndpointConfigured_RegistersSameServiceGraphAsBefore()
        {
            var services = new ServiceCollection();

            services.AddCopilotServices(BuildConfiguration(TestEndpoint));

            var feedbackDescriptors = services
                .Where(d => d.ServiceType == typeof(IFeedbackRefinementService))
                .ToList();
            Assert.AreEqual(2, feedbackDescriptors.Count, "Both fallback and real IFeedbackRefinementService should be registered.");
            Assert.AreEqual(typeof(UnavailableFeedbackRefinementService), feedbackDescriptors[0].ImplementationType);
            Assert.AreEqual(ServiceLifetime.Scoped, feedbackDescriptors[0].Lifetime);
            Assert.AreEqual(typeof(FeedbackRefinementService), feedbackDescriptors[1].ImplementationType);
            Assert.AreEqual(ServiceLifetime.Scoped, feedbackDescriptors[1].Lifetime);

            var chatFactory = services.Single(d => d.ServiceType == typeof(IChatClientFactory));
            Assert.AreEqual("Services.WebApi.AI.AzureOpenAIChatClientFactory", chatFactory.ImplementationType!.FullName);
            Assert.AreEqual(ServiceLifetime.Singleton, chatFactory.Lifetime);

            var openAI = services.Single(d => d.ServiceType == typeof(IOpenAIService));
            Assert.AreEqual(typeof(OpenAIService), openAI.ImplementationType);
            Assert.AreEqual(ServiceLifetime.Singleton, openAI.Lifetime);

            var copilot = services.Single(d => d.ServiceType == typeof(ICopilotService));
            Assert.AreEqual(typeof(CopilotService), copilot.ImplementationType);
            Assert.AreEqual(ServiceLifetime.Scoped, copilot.Lifetime);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("   ")]
        public void AddCopilotServices_EndpointInvalid_RegistersOnlyFallback(string? endpoint)
        {
            var services = new ServiceCollection();

            services.AddCopilotServices(BuildConfiguration(endpoint));

            var feedback = services.Where(d => d.ServiceType == typeof(IFeedbackRefinementService)).ToList();
            Assert.AreEqual(1, feedback.Count);
            Assert.AreEqual(typeof(UnavailableFeedbackRefinementService), feedback[0].ImplementationType);

            Assert.IsFalse(services.Any(d => d.ServiceType == typeof(IChatClientFactory)));
            Assert.IsFalse(services.Any(d => d.ServiceType == typeof(IOpenAIService)));
            Assert.IsFalse(services.Any(d => d.ServiceType == typeof(ICopilotService)));
        }

    }
}
