// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Moq;
using OpenAI.Chat;
using Repositories.Contracts;
using Services.WebApi;
using Services.WebApi.AI;
using Services.WebApi.Contracts;

namespace WebApi.Tests
{
    [TestClass]
    [DoNotParallelize]
    public class CopilotUserContextCacheTests
    {
        private static readonly MethodInfo EnrichMethod = typeof(CopilotService)
            .GetMethod("EnrichUserContextAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static MemoryCache Cache => (MemoryCache)typeof(CopilotService)
            .GetField("_userContextCache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        [DataRow(null)]
        public async Task PersonLookup_OnlyRegistersOrgLeaderCandidates(bool? isOrgLeader)
        {
            using var handler = new GraphLookupHandler();
            using var httpClient = new HttpClient(handler);
            using var services = CreateServices(httpClient);
            var service = CreateService(services);
            var gate = new OrgLeaderConfirmationGate(
                Guid.NewGuid().ToString(), "caller", "Include Pat's org and exclude Pat individually.");
            var args = isOrgLeader.HasValue
                ? System.Text.Json.JsonSerializer.Serialize(new { searchQuery = "Pat Lee", isOrgLeader })
                : """{"searchQuery":"Pat Lee"}""";
            var method = typeof(CopilotService)
                .GetMethod("ExecuteLookupPersonToolAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

            var response = await (Task<string>)method.Invoke(service, new object[] { args, gate })!;

            StringAssert.Contains(response, "pat@contoso.com");
            var confirmation = gate.Finish("Please confirm pat@contoso.com.");
            if (isOrgLeader == false)
                Assert.IsNull(confirmation);
            else
                StringAssert.Contains(confirmation!, "pat@contoso.com");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task GraphFailure_IsNotCached_AndNextRequestRetries(bool failManager)
        {
            using var handler = new GraphProfileHandler { FailNextRequest = true, FailManager = failManager };
            using var httpClient = new HttpClient(handler);
            using var services = CreateServices(httpClient);
            var objectId = Guid.NewGuid().ToString();
            var service = CreateService(services);

            await EnrichAsync(service, objectId);
            Assert.IsFalse(Cache.TryGetValue(objectId, out _), "A failed Graph lookup must not suppress retries.");

            var recovered = await EnrichAsync(service, objectId);

            Assert.AreEqual("Pat Lee", recovered.DisplayName);
            Assert.AreEqual("manager@contoso.com", recovered.ManagerEmail);
            Assert.IsTrue(Cache.TryGetValue(objectId, out _));
            Assert.AreEqual(failManager ? 4 : 3, handler.RequestCount);
            Cache.Remove(objectId);
        }

        [TestMethod]
        public async Task SuccessfulLookup_IsReusedAcrossRequests()
        {
            using var handler = new GraphProfileHandler();
            using var httpClient = new HttpClient(handler);
            using var services = CreateServices(httpClient);
            var objectId = Guid.NewGuid().ToString();

            await EnrichAsync(CreateService(services), objectId);
            var cached = await EnrichAsync(CreateService(services), objectId);

            Assert.AreEqual(2, handler.RequestCount, "The second request should not call Graph again.");
            Assert.AreEqual("Pat Lee", cached.DisplayName);
            Assert.AreEqual("manager@contoso.com", cached.ManagerEmail);
            Cache.Remove(objectId);
        }

        [TestMethod]
        public async Task ExpiredEntry_IsNotReused()
        {
            using var handler = new GraphProfileHandler();
            using var httpClient = new HttpClient(handler);
            using var services = CreateServices(httpClient);
            var objectId = Guid.NewGuid().ToString();
            Cache.Set(objectId, new CopilotUserContext { DisplayName = "Expired profile" },
                new MemoryCacheEntryOptions { AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(-1), Size = 1 });

            var refreshed = await EnrichAsync(CreateService(services), objectId);

            Assert.AreEqual(2, handler.RequestCount);
            Assert.AreEqual("Pat Lee", refreshed.DisplayName);
            Cache.Remove(objectId);
        }

        [TestMethod]
        public void Cache_HasBoundedCapacity()
        {
            var prefix = Guid.NewGuid().ToString();
            try
            {
                for (var index = 0; index < 2100; index++)
                {
                    Cache.Set($"{prefix}-{index}", new CopilotUserContext(),
                        new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                            Size = 1
                        });
                }

                Assert.IsTrue(Cache.Count <= 2000, "Profile entries must not accumulate without a size limit.");
            }
            finally
            {
                for (var index = 0; index < 2100; index++)
                {
                    Cache.Remove($"{prefix}-{index}");
                }
            }
        }

        private static ServiceProvider CreateServices(HttpClient httpClient) => new ServiceCollection()
            .AddSingleton(new GraphServiceClient(httpClient))
            .AddSingleton(Mock.Of<ISqlMembershipRepository>())
            .BuildServiceProvider();

        private static CopilotService CreateService(ServiceProvider services)
        {
            var chatFactory = new Mock<IChatClientFactory>();
            chatFactory.Setup(factory => factory.CreateChatClient()).Returns(new Mock<ChatClient>().Object);
            return new CopilotService(chatFactory.Object, services.GetRequiredService<IServiceScopeFactory>(),
                Mock.Of<IDatabaseSettingsRepository>(), NullLogger<CopilotService>.Instance);
        }

        private static async Task<CopilotUserContext> EnrichAsync(CopilotService service, string objectId)
        {
            var context = new CopilotUserContext { ObjectId = objectId };
            await (Task)EnrichMethod.Invoke(service, new object[] { context })!;
            return context;
        }

        private sealed class GraphLookupHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"value":[{"id":"pat-id","displayName":"Pat Lee","userPrincipalName":"pat@contoso.com","userType":"Member","accountEnabled":true}]}""",
                        Encoding.UTF8, "application/json")
                });
        }

        private sealed class GraphProfileHandler : HttpMessageHandler
        {
            public bool FailNextRequest { get; set; }
            public bool FailManager { get; set; }
            public int RequestCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                var isManager = request.RequestUri!.AbsolutePath.EndsWith("/manager", StringComparison.Ordinal);
                if (FailNextRequest && isManager == FailManager)
                {
                    FailNextRequest = false;
                    throw new HttpRequestException("Simulated transient Graph failure.");
                }

                var json = isManager
                    ? """{"@odata.type":"#microsoft.graph.user","id":"manager-id","displayName":"Manager","mail":"manager@contoso.com","userPrincipalName":"manager@contoso.com"}"""
                    : """{"id":"user-id","displayName":"Pat Lee","mail":"plee@contoso.com","userPrincipalName":"plee@contoso.com"}""";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }
        }
    }
}
