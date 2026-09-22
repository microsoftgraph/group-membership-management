// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Moq;
using Services.WebApi.AI;

namespace WebApi.Tests.AI
{
    [TestClass]
    [ExcludeFromCodeCoverage]
    public class AzureOpenAIChatClientFactoryTests
    {
        private const string TestEndpoint = "https://test-openai-endpoint.example.com";

        private string? _originalTokenCredentials;
        private string? _originalClientId;
        private string? _originalClientSecret;
        private string? _originalTenantId;

        [TestInitialize]
        public void Initialize()
        {
            _originalTokenCredentials = Environment.GetEnvironmentVariable("AZURE_TOKEN_CREDENTIALS");
            _originalClientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
            _originalClientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
            _originalTenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");

            // Use EnvironmentCredential for CI/CD environments where managed identity isn't available
            Environment.SetEnvironmentVariable("AZURE_TOKEN_CREDENTIALS", "EnvironmentCredential");
            Environment.SetEnvironmentVariable("AZURE_CLIENT_ID", "00000000-0000-0000-0000-000000000000");
            Environment.SetEnvironmentVariable("AZURE_CLIENT_SECRET", "dummy-secret-for-testing");
            Environment.SetEnvironmentVariable("AZURE_TENANT_ID", "00000000-0000-0000-0000-000000000000");
        }

        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable("AZURE_TOKEN_CREDENTIALS", _originalTokenCredentials);
            Environment.SetEnvironmentVariable("AZURE_CLIENT_ID", _originalClientId);
            Environment.SetEnvironmentVariable("AZURE_CLIENT_SECRET", _originalClientSecret);
            Environment.SetEnvironmentVariable("AZURE_TENANT_ID", _originalTenantId);
        }

        private static IConfiguration BuildConfiguration(string? endpoint)
        {
            var mock = new Mock<IConfiguration>();
            mock.Setup(c => c["Settings:OpenAIEndpoint"]).Returns(endpoint!);
            return mock.Object;
        }

        [TestMethod]
        public void CreateChatClient_ReturnsSameInstanceOnRepeatedCalls()
        {
            var factory = new AzureOpenAIChatClientFactory(BuildConfiguration(TestEndpoint));
            var first = factory.CreateChatClient();
            var second = factory.CreateChatClient();
            Assert.AreSame(first, second);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("   ")]
        public void Ctor_ThrowsWhenEndpointInvalid(string? endpoint)
        {
            Assert.ThrowsException<ArgumentException>(
                () => new AzureOpenAIChatClientFactory(BuildConfiguration(endpoint)));
        }

        [DataTestMethod]
        [DataRow(
            "https://example.openai.azure.com",
            "https://example.openai.azure.com/openai/v1/")]
        [DataRow(
            "https://example.openai.azure.com/",
            "https://example.openai.azure.com/openai/v1/")]
        [DataRow(
            "https://example.openai.azure.com/openai/v1",
            "https://example.openai.azure.com/openai/v1/")]
        [DataRow(
            "https://example.openai.azure.com/openai/v1/",
            "https://example.openai.azure.com/openai/v1/")]
        public void NormalizeEndpoint_ReturnsOpenAIV1Endpoint(
            string endpoint,
            string expected)
        {
            var actual = AzureOpenAIChatClientBuilder.NormalizeEndpoint(endpoint);

            Assert.AreEqual(expected, actual.AbsoluteUri);
        }
    }
}
