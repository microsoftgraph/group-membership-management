// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.FunctionBase;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Repositories.GraphGroups;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Tests.Services
{
    [TestClass]
    public sealed class GraphRepositoryDependencyInjectionTests
    {
        [TestMethod]
        public void ConfigureGraphRepository_FunctionBaseServices_InjectConfiguredRetryPolicyProvider()
        {
            var configuration = BuildFunctionConfiguration();
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            CommonServices.ConfigureCommonServices(
                services,
                configuration,
                "GroupMembershipObtainer",
                "GroupMembershipObtainer:IsDryRunEnabled",
                AppContext.BaseDirectory);
            Hosts.GroupMembershipObtainer.Program.ConfigureGraphRepository(services);
            services.AddSingleton(CreateGraphServiceClient());

            using var serviceProvider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true });
            using var scope = serviceProvider.CreateScope();
            var retryPolicyProvider =
                scope.ServiceProvider.GetRequiredService<IRetryPolicyProvider>();
            var graphRepository =
                scope.ServiceProvider.GetRequiredService<IGraphGroupRepository>();

            Assert.AreSame(
                retryPolicyProvider,
                GetInjectedRetryPolicyProvider(graphRepository));
        }

        private static IConfiguration BuildFunctionConfiguration() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["ConnectionStrings__JobsContext"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=GmmDiWrite;Integrated Security=true",
                    ["ConnectionStrings__JobsContextReadOnly"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=GmmDiRead;Integrated Security=true",
                    ["APPLICATIONINSIGHTS_CONNECTION_STRING"] =
                        "InstrumentationKey=00000000-0000-0000-0000-000000000000",
                    ["gmmServiceBus__fullyQualifiedNamespace"] =
                        "test.servicebus.windows.net"
                })
                .Build();

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
