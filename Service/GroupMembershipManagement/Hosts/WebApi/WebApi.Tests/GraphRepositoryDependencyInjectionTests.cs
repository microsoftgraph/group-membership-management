// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.GraphGroups;
using System.Collections.Generic;
using System.Reflection;

namespace WebApi.Tests
{
    [TestClass]
    public sealed class GraphRepositoryDependencyInjectionTests
    {
        [TestMethod]
        public void ConfigureGraphRepository_WebApiServices_InjectConfiguredRetryPolicyProvider()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["MaxRetryAfterAttempts"] = "3",
                    ["MaxExceptionHandlingAttempts"] = "2"
                })
                .Build();

            using var serviceProvider = CreateServiceProvider(configuration);
            using var scope = serviceProvider.CreateScope();
            var graphServiceAttemptsValue =
                scope.ServiceProvider.GetRequiredService<IGraphServiceAttemptsValue>();
            var retryPolicyProvider =
                scope.ServiceProvider.GetRequiredService<IRetryPolicyProvider>();
            var graphRepository =
                scope.ServiceProvider.GetRequiredService<IGraphGroupRepository>();

            Assert.AreEqual(3, graphServiceAttemptsValue.MaxRetryAfterAttempts);
            Assert.AreEqual(2, graphServiceAttemptsValue.MaxExceptionHandlingAttempts);
            Assert.AreSame(
                retryPolicyProvider,
                GetInjectedRetryPolicyProvider(graphRepository));
        }

        [TestMethod]
        public void ConfigureGraphRepository_MissingAttemptsConfiguration_UsesDefaults()
        {
            var configuration = new ConfigurationBuilder().Build();

            using var serviceProvider = CreateServiceProvider(configuration);
            var graphServiceAttemptsValue =
                serviceProvider.GetRequiredService<IGraphServiceAttemptsValue>();

            Assert.AreEqual(4, graphServiceAttemptsValue.MaxRetryAfterAttempts);
            Assert.AreEqual(2, graphServiceAttemptsValue.MaxExceptionHandlingAttempts);
        }

        private static ServiceProvider CreateServiceProvider(
            IConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(configuration);
            services.AddSingleton(new TelemetryClient(
                TelemetryConfiguration.CreateDefault()));
            WebApi.Program.ConfigureGraphRepository(services, configuration);
            services.AddSingleton(CreateGraphServiceClient());

            return services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true });
        }

        private static GraphServiceClient CreateGraphServiceClient()
        {
            var requestAdapter = new Mock<IRequestAdapter>();
            return new GraphServiceClient(
                requestAdapter.Object,
                "https://graph.microsoft.com/v1.0");
        }

        private static object GetInjectedRetryPolicyProvider(
            IGraphGroupRepository graphRepository)
        {
            var membershipReader = typeof(GraphGroupRepository)
                .GetField(
                    "_graphGroupMembershipReader",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(graphRepository);
            return membershipReader
                .GetType()
                .GetField(
                    "_retryPolicyProvider",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(membershipReader);
        }
    }
}
