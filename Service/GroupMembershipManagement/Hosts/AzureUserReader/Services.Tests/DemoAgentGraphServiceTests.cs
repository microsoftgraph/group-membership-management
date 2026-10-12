// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Http.HttpClientLibrary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class DemoAgentGraphServiceTests
    {
        private const string Identity = "{\"id\":\"identity\",\"displayName\":\"GMM Demo Agent 1\",\"agentIdentityBlueprintId\":\"blueprint\",\"sponsors\":[{\"id\":\"manager\"}]}";
        private const string User = "{\"@odata.type\":\"#microsoft.graph.agentUser\",\"id\":\"user\",\"userPrincipalName\":\"gmm-demo-agent-1@contoso.onmicrosoft.com\",\"identityParentId\":\"identity\"}";
        private static DemoAgentSpec Spec => new()
        {
            DisplayName = "GMM Demo Agent 1", ManagerId = "manager",
            MailNickname = "gmm-demo-agent-1", UserPrincipalName = "gmm-demo-agent-1@contoso.onmicrosoft.com"
        };

        private sealed class Transport : HttpMessageHandler
        {
            public readonly Queue<(HttpMethod Method, string Path, HttpStatusCode Status, string Body)> Responses = new();
            public readonly List<(HttpMethod Method, string Path)> Requests = new();

            public void Expect(HttpMethod method, string path, HttpStatusCode status, string body = "{}")
                => Responses.Enqueue((method, path, status, body));

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests.Add((request.Method, request.RequestUri.AbsolutePath));
                Assert.IsTrue(Responses.Count > 0, "Unexpected physical HTTP request.");
                var expected = Responses.Dequeue();
                Assert.AreEqual(expected.Method, request.Method);
                Assert.AreEqual(expected.Path, request.RequestUri.AbsolutePath, "Unexpected physical HTTP request or replay.");
                var response = new HttpResponseMessage(expected.Status)
                {
                    RequestMessage = request,
                    Content = new StringContent(expected.Body, Encoding.UTF8, "application/json")
                };
                response.Headers.TryAddWithoutValidation("Retry-After", "0");
                return Task.FromResult(response);
            }
        }

        [DataTestMethod]
        [DataRow(false, 504)]
        [DataRow(false, 503)]
        [DataRow(true, 504)]
        [DataRow(true, 503)]
        public async Task Transport_AmbiguousCreation_ReadsBackWithoutPhysicalPostReplay(bool failUser, int status)
        {
            using var transport = new Transport();
            const string identityPath = "/beta/servicePrincipals/microsoft.graph.agentIdentity";
            const string userPath = "/beta/users/microsoft.graph.agentUser";
            var lookupPath = "/beta/users/" + Uri.EscapeDataString(Spec.UserPrincipalName);
            transport.Expect(HttpMethod.Get, identityPath, HttpStatusCode.OK, "{\"value\":[]}");
            transport.Expect(HttpMethod.Get, lookupPath, HttpStatusCode.NotFound);
            transport.Expect(HttpMethod.Post, identityPath, failUser ? HttpStatusCode.Created : (HttpStatusCode)status,
                failUser ? "{\"id\":\"identity\"}" : "{}");
            if (!failUser)
                transport.Expect(HttpMethod.Get, identityPath, HttpStatusCode.OK, "{\"value\":[" + Identity + "]}");
            transport.Expect(HttpMethod.Post, userPath, failUser ? (HttpStatusCode)status : HttpStatusCode.Created,
                failUser ? "{}" : "{\"id\":\"user\"}");
            if (failUser)
                transport.Expect(HttpMethod.Get, lookupPath, HttpStatusCode.OK, User);

            using var httpClient = KiotaClientFactory.Create(transport);
            using var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient);
            var delays = 0;
            var service = new DemoAgentGraphService(new GraphServiceClient(adapter), _ =>
            {
                delays++;
                return Task.CompletedTask;
            });

            Assert.AreEqual("user", await service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(1, transport.Requests.FindAll(r => r.Method == HttpMethod.Post && r.Path == identityPath).Count);
            Assert.AreEqual(1, transport.Requests.FindAll(r => r.Method == HttpMethod.Post && r.Path == userPath).Count);
            Assert.AreEqual(1, delays);
            Assert.AreEqual(0, transport.Responses.Count);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Transport_BlueprintAndPrincipalCreation_DoNotReplay(bool existingBlueprint)
        {
            using var transport = new Transport();
            const string blueprintPath = "/beta/applications/microsoft.graph.agentIdentityBlueprint";
            const string principalPath = "/beta/servicePrincipals/microsoft.graph.agentIdentityBlueprintPrincipal";
            transport.Expect(HttpMethod.Get, blueprintPath, HttpStatusCode.OK,
                existingBlueprint ? "{\"value\":[{\"appId\":\"blueprint\"}]}" : "{\"value\":[]}");
            if (existingBlueprint)
                transport.Expect(HttpMethod.Get, principalPath, HttpStatusCode.OK, "{\"value\":[]}");
            transport.Expect(HttpMethod.Post, existingBlueprint ? principalPath : blueprintPath, HttpStatusCode.GatewayTimeout);
            using var httpClient = KiotaClientFactory.Create(transport);
            using var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient);
            var service = new DemoAgentGraphService(new GraphServiceClient(adapter));

            await Assert.ThrowsExceptionAsync<Microsoft.Graph.Models.ODataErrors.ODataError>(
                () => service.EnsureBlueprintAsync(sponsorId: "manager"));
            Assert.AreEqual(1, transport.Requests.FindAll(r => r.Method == HttpMethod.Post).Count);
            Assert.AreEqual(0, transport.Responses.Count);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task Transport_GetAndIdempotentPut_KeepNormalRetries(bool managerWrite)
        {
            using var transport = new Transport();
            var method = managerWrite ? HttpMethod.Put : HttpMethod.Get;
            var path = managerWrite ? "/beta/users/user/manager/$ref" : "/beta/users/microsoft.graph.agentUser";
            transport.Expect(method, path, HttpStatusCode.ServiceUnavailable);
            transport.Expect(method, path, managerWrite ? HttpStatusCode.NoContent : HttpStatusCode.OK, "{\"value\":[]}");
            using var httpClient = KiotaClientFactory.Create(transport);
            using var adapter = new HttpClientRequestAdapter(new AnonymousAuthenticationProvider(), httpClient: httpClient);
            var service = new DemoAgentGraphService(new GraphServiceClient(adapter));

            if (managerWrite)
                await service.SetManagerAsync("user", "manager");
            else
                Assert.AreEqual(0, (await service.GetDemoAgentsAsync()).Count);
            Assert.AreEqual(2, transport.Requests.Count);
            Assert.AreEqual(0, transport.Responses.Count);
        }

        private sealed class Directory
        {
            public string Identities = "{\"value\":[]}";
            public string ExistingUser;
            public string NextIdentities;
            public Exception UserFailure;
            public Exception IdentityFailure;
            public bool CommitUserBeforeFailure;
            public bool CommitIdentityBeforeFailure;
            public int IdentityPosts;
            public int UserPosts;
            public int Delays;
            public int HiddenUserReads;
            public readonly List<RequestInformation> Requests = new();
            public DemoAgentGraphService Service { get; }

            public Directory()
            {
                var adapter = new Mock<IRequestAdapter>();
                adapter.SetupProperty(a => a.BaseUrl, "https://graph.microsoft.com/v1.0");
                adapter.Setup(a => a.SendPrimitiveAsync<Stream>(
                    It.IsAny<RequestInformation>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((RequestInformation request, Dictionary<string, ParsableFactory<IParsable>> errors, CancellationToken token) =>
                    {
                        token.ThrowIfCancellationRequested();
                        Requests.Add(request);
                        Assert.AreEqual("graph.microsoft.com", request.URI.Host);
                        Assert.IsTrue(request.URI.AbsolutePath.StartsWith("/beta/"));
                        if (request.HttpMethod == Method.GET)
                        {
                            if (request.URI.AbsolutePath == "/beta/identity-page-two")
                                return Task.FromResult(Stream(NextIdentities));
                            if (request.URI.AbsolutePath.EndsWith("/servicePrincipals/microsoft.graph.agentIdentity"))
                            {
                                var query = Uri.UnescapeDataString(request.URI.Query);
                                StringAssert.Contains(query, "$filter=displayName eq 'GMM Demo Agent 1'");
                                StringAssert.Contains(query, "$expand=sponsors($select=id)");
                                return Task.FromResult(Stream(Identities));
                            }
                            if (ExistingUser == null || HiddenUserReads-- > 0)
                                throw new ApiException { ResponseStatusCode = 404 };
                            return Task.FromResult(Stream(ExistingUser));
                        }
                        Assert.AreEqual(Method.POST, request.HttpMethod);
                        if (request.URI.AbsolutePath.EndsWith("/servicePrincipals/microsoft.graph.agentIdentity"))
                        {
                            IdentityPosts++;
                            if (IdentityFailure == null || CommitIdentityBeforeFailure)
                                Identities = "{\"value\":[" + Identity + "]}";
                            if (IdentityFailure != null)
                                throw IdentityFailure;
                            return Task.FromResult(Stream("{\"id\":\"identity\"}"));
                        }
                        UserPosts++;
                        using var body = JsonDocument.Parse(request.Content);
                        Assert.AreEqual("identity", body.RootElement.GetProperty("identityParentId").GetString());
                        if (UserFailure == null || CommitUserBeforeFailure)
                            ExistingUser = User;
                        if (UserFailure != null)
                            throw UserFailure;
                        return Task.FromResult(Stream("{\"id\":\"user\"}"));
                    });
                Service = new DemoAgentGraphService(new GraphServiceClient(adapter.Object), _ =>
                {
                    Delays++;
                    return Task.CompletedTask;
                });
            }

            private static Stream Stream(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));
        }

        [TestMethod]
        public async Task NormalCreation_PostsEachObjectOnce()
        {
            var directory = new Directory();
            Assert.AreEqual("user", await directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(1, directory.IdentityPosts);
            Assert.AreEqual(1, directory.UserPosts);
        }

        [TestMethod]
        public async Task FailedUserCreation_RetryReusesOrphan()
        {
            var directory = new Directory { UserFailure = new ApiException { ResponseStatusCode = 400 } };
            await Assert.ThrowsExceptionAsync<ApiException>(() => directory.Service.CreateAgentAsync(Spec, "blueprint"));
            directory.UserFailure = null;
            Assert.AreEqual("user", await directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(1, directory.IdentityPosts);
            Assert.AreEqual(2, directory.UserPosts);
        }

        [TestMethod]
        public async Task AmbiguousUserSuccess_ReadsBackWithoutReposting()
        {
            var directory = new Directory { UserFailure = new HttpRequestException("Lost response"), CommitUserBeforeFailure = true };
            Assert.AreEqual("user", await directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual("user", await directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(1, directory.IdentityPosts);
            Assert.AreEqual(1, directory.UserPosts);
            Assert.AreEqual(1, directory.Delays);
        }

        [TestMethod]
        public async Task AmbiguousIdentitySuccess_ReadsBackWithoutReposting()
        {
            var directory = new Directory { IdentityFailure = new HttpRequestException("Lost response"), CommitIdentityBeforeFailure = true };
            Assert.AreEqual("user", await directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(1, directory.IdentityPosts);
            Assert.AreEqual(1, directory.UserPosts);
        }

        [TestMethod]
        public async Task AmbiguousIdentityMissing_PropagatesWithoutSecondPost()
        {
            var directory = new Directory { IdentityFailure = new HttpRequestException("Lost response") };
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(1, directory.IdentityPosts);
            Assert.AreEqual(0, directory.UserPosts);
            Assert.AreEqual(3, directory.Delays);
        }

        [DataTestMethod]
        [DataRow("blueprint", "other-manager", false)]
        [DataRow("other-blueprint", "manager", false)]
        [DataRow("blueprint", "manager", true)]
        public async Task ConflictingIdentities_FailWithoutWrites(string blueprint, string sponsor, bool duplicate)
        {
            var identity = Identity.Replace("\"blueprint\"", $"\"{blueprint}\"").Replace("\"manager\"", $"\"{sponsor}\"");
            var directory = new Directory { Identities = "{\"value\":[" + identity + (duplicate ? "," + identity : "") + "]}" };
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(0, directory.IdentityPosts);
            Assert.AreEqual(0, directory.UserPosts);
        }

        [DataTestMethod]
        [DataRow("#microsoft.graph.user", "identity")]
        [DataRow("#microsoft.graph.agentUser", "other-identity")]
        public async Task ConflictingUsers_FailWithoutWrites(string type, string parent)
        {
            var directory = new Directory
            {
                Identities = "{\"value\":[" + Identity + "]}",
                ExistingUser = User.Replace("#microsoft.graph.agentUser", type).Replace("\"identity\"", $"\"{parent}\"")
            };
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(0, directory.IdentityPosts);
            Assert.AreEqual(0, directory.UserPosts);
        }

        [TestMethod]
        public async Task AmbiguousUserMissing_PropagatesAndRetainsIdentityForRetry()
        {
            var directory = new Directory { UserFailure = new HttpRequestException("Lost response") };
            await Assert.ThrowsExceptionAsync<HttpRequestException>(() => directory.Service.CreateAgentAsync(Spec, "blueprint"));
            directory.UserFailure = null;
            Assert.AreEqual("user", await directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(1, directory.IdentityPosts);
            Assert.AreEqual(2, directory.UserPosts);
            Assert.AreEqual(3, directory.Delays);
        }

        [TestMethod]
        public async Task EventualUserVisibility_RecoversOnThirdRead()
        {
            var directory = new Directory
            {
                UserFailure = new HttpRequestException("Lost response"), CommitUserBeforeFailure = true,
                HiddenUserReads = 2
            };
            Assert.AreEqual("user", await directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(3, directory.Delays);
            Assert.AreEqual(1, directory.IdentityPosts);
            Assert.AreEqual(1, directory.UserPosts);
        }

        [TestMethod]
        public async Task DuplicateUpnBadRequest_RequiresMatchingReadback()
        {
            var directory = new Directory
            {
                UserFailure = new ApiException { ResponseStatusCode = 400 }, CommitUserBeforeFailure = true
            };
            Assert.AreEqual("user", await directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(1, directory.UserPosts);
        }

        [TestMethod]
        public async Task PermissionFailure_IsNotRetriedOrHidden()
        {
            var directory = new Directory { UserFailure = new ApiException { ResponseStatusCode = 403 } };
            await Assert.ThrowsExceptionAsync<ApiException>(() => directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(0, directory.Delays);
            Assert.AreEqual(1, directory.IdentityPosts);
        }

        [TestMethod]
        public async Task ExistingUserWithoutMatchingIdentity_FailsBeforeIdentityPost()
        {
            var directory = new Directory { ExistingUser = User };
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(0, directory.IdentityPosts);
            Assert.AreEqual(0, directory.UserPosts);
        }

        [TestMethod]
        public async Task MissingSponsorData_FailsClosed()
        {
            var directory = new Directory
            {
                Identities = "{\"value\":[{\"id\":\"identity\",\"displayName\":\"GMM Demo Agent 1\",\"agentIdentityBlueprintId\":\"blueprint\"}]}"
            };
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(0, directory.IdentityPosts);
        }

        [TestMethod]
        public async Task DuplicateIdentityOnLaterPage_FailsBeforeWrites()
        {
            var directory = new Directory
            {
                Identities = "{\"value\":[" + Identity + "],\"@odata.nextLink\":\"https://graph.microsoft.com/beta/identity-page-two\"}",
                NextIdentities = "{\"value\":[" + Identity + "]}"
            };
            await Assert.ThrowsExceptionAsync<InvalidDataException>(() => directory.Service.CreateAgentAsync(Spec, "blueprint"));
            Assert.AreEqual(0, directory.IdentityPosts);
            Assert.AreEqual(0, directory.UserPosts);
            Assert.AreEqual(2, directory.Requests.Count);
        }

        [TestMethod]
        public async Task Cancellation_IsNotConvertedToRecovery()
        {
            var directory = new Directory();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(
                () => directory.Service.CreateAgentAsync(Spec, "blueprint", cancellation.Token));
            Assert.AreEqual(0, directory.IdentityPosts);
            Assert.AreEqual(0, directory.Delays);
        }
    }
}
