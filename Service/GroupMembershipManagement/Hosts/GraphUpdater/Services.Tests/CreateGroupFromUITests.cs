// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Common.DependencyInjection;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NewGroupType = Models.NewGroupType;
using Repositories.Contracts;
using Repositories.GraphGroups;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class CreateGroupFromUITests
    {
        private const string GRAPH_API_V1_BASE_URL = "https://graph.microsoft.com/v1.0";
        private const string DirectoryObjectUrlPrefix = "https://graph.microsoft.com/v1.0/directoryObjects/";
        private const string ClientAppId = "11111111-1111-1111-1111-111111111111";
        private const string ManagedIdentityAppId = "22222222-2222-2222-2222-222222222222";

        private Mock<IRequestAdapter> _requestAdapter;
        private Mock<IGraphRepositorySettings> _graphRepositorySettings;
        private List<RecordedRequest> _requests;
        private Guid _createdGroupId;

        private sealed class RecordedRequest
        {
            public string Method { get; init; }
            public string Uri { get; init; }
            public string Body { get; init; }
        }

        [TestInitialize]
        public void Setup()
        {
            _requests = new List<RecordedRequest>();
            _createdGroupId = Guid.NewGuid();

            _requestAdapter = new Mock<IRequestAdapter>();
            _requestAdapter.SetupProperty(x => x.BaseUrl).SetReturnsDefault(GRAPH_API_V1_BASE_URL);
            // RequestInformation.SetContentFromParsable asks the adapter for this factory; a bare
            // mock returns null and the serialization throws before the request is ever recorded.
            _requestAdapter.Setup(x => x.SerializationWriterFactory).Returns(new JsonSerializationWriterFactory());

            _graphRepositorySettings = new Mock<IGraphRepositorySettings>();
            _graphRepositorySettings.Setup(x => x.ConcurrentAddRequests).Returns(10);
            _graphRepositorySettings.Setup(x => x.ConcurrentRemoveRequests).Returns(10);

            _requestAdapter.Setup(x => x.SendAsync(It.IsAny<RequestInformation>(),
                                                   It.IsAny<ParsableFactory<Group>>(),
                                                   It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                                                   It.IsAny<CancellationToken>()))
                           .Callback<object, object, object, object>((request, factory, errorMapping, cancellationToken) =>
                           {
                               var requestInformation = request as RequestInformation;
                               Record(requestInformation);
                               RespondWithGroup(requestInformation, HttpStatusCode.OK);
                           });

            _requestAdapter.Setup(x => x.SendNoContentAsync(It.IsAny<RequestInformation>(),
                                                            It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                                                            It.IsAny<CancellationToken>()))
                           .Callback<object, object, object>((request, errorMapping, cancellationToken) =>
                           {
                               var requestInformation = request as RequestInformation;
                               Record(requestInformation);
                               SetResponse(requestInformation, new HttpResponseMessage(HttpStatusCode.NoContent));
                           });
        }

        [TestMethod]
        [DataRow(NewGroupType.Microsoft365, null)]
        [DataRow(NewGroupType.Microsoft365, "")]
        [DataRow(NewGroupType.Microsoft365, " ")]
        [DataRow(NewGroupType.Microsoft365, "Project documentation")]
        [DataRow(NewGroupType.Security, null)]
        [DataRow(NewGroupType.Security, "")]
        [DataRow(NewGroupType.Security, " ")]
        [DataRow(NewGroupType.Security, "Project documentation")]
        public async Task PreservesDescriptionAndOwnerBindings(NewGroupType groupType, string description)
        {
            var ownerId = Guid.NewGuid();
            var gmmOwnerId = Guid.NewGuid().ToString();
            var repository = CreateRepository(servicePrincipalObjectId: gmmOwnerId);

            var group = await repository.CreateGroupFromUI("Finance Team", ownerId, "finance+team", groupType, description);

            Assert.IsNotNull(group);
            var body = GetCreateRequestBody();
            Assert.AreEqual(description ?? "Finance Team", body["description"].GetValue<string>());
            Assert.AreEqual("finance+team", body["mailNickname"].GetValue<string>());
            Assert.AreEqual(groupType == NewGroupType.Microsoft365, body["mailEnabled"].GetValue<bool>());
            Assert.AreEqual(groupType == NewGroupType.Security, body["securityEnabled"].GetValue<bool>());
            CollectionAssert.AreEquivalent(
                new List<string> { $"{DirectoryObjectUrlPrefix}{ownerId}", $"{DirectoryObjectUrlPrefix}{gmmOwnerId}" },
                GetOwnerBindings());
        }

        [TestMethod]
        [DataRow(HttpStatusCode.BadRequest, false)]
        [DataRow(HttpStatusCode.Unauthorized, false)]
        [DataRow(HttpStatusCode.Forbidden, false)]
        [DataRow(HttpStatusCode.NotFound, false)]
        [DataRow(HttpStatusCode.MethodNotAllowed, false)]
        [DataRow(HttpStatusCode.Conflict, false)]
        [DataRow(HttpStatusCode.UnprocessableEntity, false)]
        [DataRow(HttpStatusCode.RequestTimeout, false)]
        [DataRow(HttpStatusCode.TooManyRequests, false)]
        [DataRow(HttpStatusCode.InternalServerError, false)]
        [DataRow(HttpStatusCode.BadRequest, true)]
        [DataRow(HttpStatusCode.InternalServerError, true)]
        public async Task PreservesTheCreateResponseStatus(HttpStatusCode statusCode, bool sdkError)
        {
            var repository = CreateRepository(servicePrincipalObjectId: Guid.NewGuid().ToString());
            _requestAdapter.Setup(x => x.SendAsync(It.IsAny<RequestInformation>(),
                                                   It.IsAny<ParsableFactory<Group>>(),
                                                   It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                                                   It.IsAny<CancellationToken>()))
                .Callback<object, object, object, object>((request, factory, errorMapping, cancellationToken) =>
                {
                    var requestInformation = (RequestInformation)request;
                    Record(requestInformation);
                    if (sdkError) throw new ODataError { ResponseStatusCode = (int)statusCode };
                    SetResponse(requestInformation, JsonResponse(statusCode, "{}"));
                });

            var exception = await Assert.ThrowsExceptionAsync<HttpRequestException>(
                () => repository.CreateGroupFromUI("Finance Team", Guid.NewGuid(), "financeteam"));

            Assert.AreEqual(statusCode, exception.StatusCode);
            Assert.AreEqual(1, _requests.Count(x => x.Method == "POST"));
            Assert.IsFalse(_requests.Any(x => x.Method == "GET" && x.Uri.Contains("/groups/")));
        }

        [TestMethod]
        public async Task DoesNotTreatATransportFailureAsAConfirmedRejection()
        {
            var repository = CreateRepository(servicePrincipalObjectId: Guid.NewGuid().ToString());
            _requestAdapter.Setup(x => x.SendAsync(It.IsAny<RequestInformation>(),
                                                   It.IsAny<ParsableFactory<Group>>(),
                                                   It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                                                   It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("Connection lost"));

            var group = await repository.CreateGroupFromUI("Finance Team", Guid.NewGuid(), "financeteam");

            Assert.IsNull(group);
        }

        [TestMethod]
        public async Task CreatesAMicrosoftThreeSixFiveGroup()
        {
            var repository = CreateRepository(servicePrincipalObjectId: Guid.NewGuid().ToString());

            var group = await repository.CreateGroupFromUI("Finance Team", Guid.NewGuid(), "financeteam");

            Assert.IsNotNull(group);

            var body = GetCreateRequestBody();
            Assert.IsTrue(body["mailEnabled"].GetValue<bool>(), "A Microsoft 365 group must be mail enabled.");
            Assert.IsFalse(body["securityEnabled"].GetValue<bool>(), "A Microsoft 365 group must not be security enabled.");
            CollectionAssert.AreEqual(new List<string> { "Unified" }, body["groupTypes"].AsArray().Select(x => x.GetValue<string>()).ToList());
            Assert.AreEqual("financeteam", body["mailNickname"].GetValue<string>());
            Assert.AreEqual("Finance Team", body["displayName"].GetValue<string>());
            Assert.AreEqual("Finance Team", body["description"].GetValue<string>(), "Legacy callers retain the name description.");
        }

        [TestMethod]
        public async Task CreatesASecurityGroup()
        {
            var repository = CreateRepository(servicePrincipalObjectId: Guid.NewGuid().ToString());

            var group = await repository.CreateGroupFromUI("Finance Admins", Guid.NewGuid(), "financeadmins", NewGroupType.Security);

            Assert.IsNotNull(group);

            var body = GetCreateRequestBody();
            Assert.IsFalse(body["mailEnabled"].GetValue<bool>(), "A security group must not be mail enabled.");
            Assert.IsTrue(body["securityEnabled"].GetValue<bool>(), "A security group must be security enabled.");
            Assert.AreEqual(0, body["groupTypes"].AsArray().Count, "A security group carries no group types; Unified would make it a Microsoft 365 group.");
            Assert.AreEqual("financeadmins", body["mailNickname"].GetValue<string>(), "Graph requires a mail nickname on a security group too.");
            Assert.AreEqual("Finance Admins", body["displayName"].GetValue<string>());
            Assert.AreEqual("Finance Admins", body["description"].GetValue<string>(), "Omitting description retains the group name for security groups too.");
        }

        [TestMethod]
        public async Task BindsBothOwnersWhenCreatingASecurityGroup()
        {
            var gmmOwnerObjectId = Guid.NewGuid().ToString();
            var requesterId = Guid.NewGuid();
            var repository = CreateRepository(servicePrincipalObjectId: gmmOwnerObjectId);

            await repository.CreateGroupFromUI("Finance Admins", requesterId, "financeadmins", NewGroupType.Security);

            var owners = GetOwnerBindings();
            CollectionAssert.AreEquivalent(
                new List<string> { $"{DirectoryObjectUrlPrefix}{requesterId}", $"{DirectoryObjectUrlPrefix}{gmmOwnerObjectId}" },
                owners,
                "GMM must own the security groups it creates, or it cannot manage their membership.");
        }

        [TestMethod]
        public async Task DefaultsToAMicrosoftThreeSixFiveGroupWhenNoTypeIsRequested()
        {
            var repository = CreateRepository(servicePrincipalObjectId: Guid.NewGuid().ToString());

            await repository.CreateGroupFromUI("Finance Team", Guid.NewGuid(), "financeteam");

            var body = GetCreateRequestBody();
            CollectionAssert.AreEqual(new List<string> { "Unified" }, body["groupTypes"].AsArray().Select(x => x.GetValue<string>()).ToList(),
                "Omitting the type must keep the behaviour that existed before security groups were supported.");
        }

        [TestMethod]
        public async Task BindsBothOwnersOnTheCreateRequest()
        {
            var gmmOwnerObjectId = Guid.NewGuid().ToString();
            var requesterId = Guid.NewGuid();
            var repository = CreateRepository(servicePrincipalObjectId: gmmOwnerObjectId);

            await repository.CreateGroupFromUI("Finance Team", requesterId, "financeteam");

            var owners = GetOwnerBindings();
            Assert.AreEqual(2, owners.Count);
            CollectionAssert.AreEquivalent(
                new List<string> { $"{DirectoryObjectUrlPrefix}{requesterId}", $"{DirectoryObjectUrlPrefix}{gmmOwnerObjectId}" },
                owners);
        }

        [TestMethod]
        public async Task DoesNotIssueASeparateOwnerAssignmentRequest()
        {
            var repository = CreateRepository(servicePrincipalObjectId: Guid.NewGuid().ToString());

            await repository.CreateGroupFromUI("Finance Team", Guid.NewGuid(), "financeteam");

            Assert.IsFalse(_requests.Any(x => x.Uri.Contains("/owners/$ref", StringComparison.OrdinalIgnoreCase)),
                "Owners must be bound on the create request; production is only granted Group.Create.");
        }

        [TestMethod]
        public async Task CollapsesTheBindingWhenTheRequesterIsGmm()
        {
            var sharedId = Guid.NewGuid();
            var repository = CreateRepository(servicePrincipalObjectId: sharedId.ToString());

            await repository.CreateGroupFromUI("Finance Team", sharedId, "financeteam");

            var owners = GetOwnerBindings();
            Assert.AreEqual(1, owners.Count, "Microsoft Graph rejects a repeated owner binding.");
        }

        [TestMethod]
        [DataRow(AuthenticationType.ClientSecret, ClientAppId)]
        [DataRow(AuthenticationType.Certificate, ClientAppId)]
        [DataRow(AuthenticationType.UserAssignedManagedIdentity, ManagedIdentityAppId)]
        public async Task ResolvesTheOwnerUsingTheConfiguredAppId(AuthenticationType authenticationType, string expectedAppId)
        {
            var servicePrincipalObjectId = Guid.NewGuid().ToString();
            var repository = CreateRepository(servicePrincipalObjectId, authenticationType);

            await repository.CreateGroupFromUI("Finance Team", Guid.NewGuid(), "financeteam");

            var owners = GetOwnerBindings();
            CollectionAssert.Contains(owners, $"{DirectoryObjectUrlPrefix}{servicePrincipalObjectId}");
            CollectionAssert.DoesNotContain(owners, $"{DirectoryObjectUrlPrefix}{expectedAppId}",
                "The owner binding must use the resolved directory object id, not the application id.");

            var lookup = _requests.Single(x => x.Uri.Contains("/servicePrincipals", StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual("GET", lookup.Method);
            StringAssert.Contains(Uri.UnescapeDataString(lookup.Uri), $"appId eq '{expectedAppId}'");
            Assert.AreEqual(lookup, _requests.First(), "GMM must be resolved before creating the group.");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ResolvesOwnerCredentialsThroughDependencyInjection(bool registerRepositorySettings)
        {
            var servicePrincipalObjectId = Guid.NewGuid().ToString();
            SetupServicePrincipalLookup(servicePrincipalObjectId);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(new GraphServiceClient(_requestAdapter.Object, GRAPH_API_V1_BASE_URL));
            services.AddSingleton(new TelemetryClient(new TelemetryConfiguration("instrumentationkey")));
            services.AddSingleton<IOptions<GraphCredentials>>(Options.Create(new GraphCredentials
            {
                AuthenticationType = AuthenticationType.UserAssignedManagedIdentity,
                UserAssignedManagedIdentityClientId = ManagedIdentityAppId
            }));
            if (registerRepositorySettings)
            {
                services.AddSingleton(_graphRepositorySettings.Object);
            }
            services.AddScoped<IGraphGroupRepository, GraphGroupRepository>();

            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IGraphGroupRepository>();

            await repository.CreateGroupFromUI("Finance Team", Guid.NewGuid(), "financeteam");

            CollectionAssert.Contains(GetOwnerBindings(), $"{DirectoryObjectUrlPrefix}{servicePrincipalObjectId}");
            var lookup = _requests.Single(x => x.Uri.Contains("/servicePrincipals", StringComparison.OrdinalIgnoreCase));
            StringAssert.Contains(Uri.UnescapeDataString(lookup.Uri), $"appId eq '{ManagedIdentityAppId}'");
        }

        [TestMethod]
        public async Task ThrowsWithoutCreatingAGroupWhenGmmCannotBeResolved()
        {
            var repository = CreateRepository(servicePrincipalObjectId: null);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => repository.CreateGroupFromUI("Finance Team", Guid.NewGuid(), "financeteam"));

            Assert.IsFalse(_requests.Any(x => x.Method == "POST" && x.Uri.TrimEnd('/').EndsWith("/groups", StringComparison.OrdinalIgnoreCase)),
                "No group may be created when GMM cannot be named as an owner.");
        }

        [TestMethod]
        public async Task ReturnsTheGroupWhenTheReadBackIsInitiallyNotFound()
        {
            SetupCreateFollowedByReadBack(notFoundReadBacks: 2);

            var repository = CreateRepository(servicePrincipalObjectId: Guid.NewGuid().ToString());

            var group = await repository.CreateGroupFromUI("Finance Team", Guid.NewGuid(), "financeteam");

            Assert.IsNotNull(group, "Directory replication delay must not be reported as a failed creation.");
            Assert.AreEqual(_createdGroupId, group.ObjectId);
        }

        [TestMethod]
        [DataRow(HttpStatusCode.NotFound)]
        [DataRow(HttpStatusCode.BadRequest)]
        public async Task ReturnsTheGroupWhenTheReadBackNeverSucceeds(HttpStatusCode readBackStatus)
        {
            SetupCreateFollowedByReadBack(notFoundReadBacks: int.MaxValue, readBackStatus: readBackStatus);

            var repository = CreateRepository(servicePrincipalObjectId: Guid.NewGuid().ToString());

            var group = await repository.CreateGroupFromUI("Finance Team", Guid.NewGuid(), "financeteam");

            Assert.IsNotNull(group,
                "Graph already confirmed creation with a 201; returning null here would orphan the group.");
            Assert.AreEqual(_createdGroupId, group.ObjectId);
        }

        /// <summary>
        /// Answers the create with a 201 and the first <paramref name="notFoundReadBacks"/> read-backs with a failure,
        /// reproducing the directory replication delay that follows a successful group creation.
        /// </summary>
        private void SetupCreateFollowedByReadBack(int notFoundReadBacks, HttpStatusCode readBackStatus = HttpStatusCode.NotFound)
        {
            var readBackCount = 0;

            _requestAdapter.Setup(x => x.SendAsync(It.IsAny<RequestInformation>(),
                                                   It.IsAny<ParsableFactory<Group>>(),
                                                   It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                                                   It.IsAny<CancellationToken>()))
                           .Callback<object, object, object, object>((request, factory, errorMapping, cancellationToken) =>
                           {
                               var requestInformation = request as RequestInformation;
                               Record(requestInformation);

                               if (requestInformation.HttpMethod == Method.POST)
                               {
                                   RespondWithGroup(requestInformation, HttpStatusCode.Created);
                                   return;
                               }

                               if (++readBackCount <= notFoundReadBacks)
                               {
                                   SetResponse(requestInformation, JsonResponse(readBackStatus, "{}"));
                                   return;
                               }

                               RespondWithGroup(requestInformation, HttpStatusCode.OK);
                           });
        }

        [TestMethod]
        public async Task ThrowsWithoutCreatingAGroupWhenTheLookupFails()
        {
            var repository = CreateRepository(servicePrincipalObjectId: Guid.NewGuid().ToString());
            SetupServicePrincipalLookup(null, HttpStatusCode.Forbidden);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => repository.CreateGroupFromUI("Finance Team", Guid.NewGuid(), "financeteam"));

            Assert.IsFalse(_requests.Any(x => x.Method == "POST"),
                "A failed identity lookup must prevent group creation.");
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public async Task ThrowsWithoutCreatingAGroupWhenTheAppIdIsMissing(string appId)
        {
            var repository = CreateRepository(servicePrincipalObjectId: Guid.NewGuid().ToString(),
                credentials: new GraphCredentials
                {
                    AuthenticationType = AuthenticationType.UserAssignedManagedIdentity,
                    UserAssignedManagedIdentityClientId = appId
                });

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => repository.CreateGroupFromUI("Finance Team", Guid.NewGuid(), "financeteam"));

            Assert.AreEqual(0, _requests.Count, "A missing application id must prevent any Graph request.");
        }

        private GraphGroupRepository CreateRepository(string servicePrincipalObjectId,
            AuthenticationType authenticationType = AuthenticationType.UserAssignedManagedIdentity,
            GraphCredentials credentials = null)
        {
            SetupServicePrincipalLookup(servicePrincipalObjectId);
            var graphServiceClient = new Mock<GraphServiceClient>(_requestAdapter.Object, GRAPH_API_V1_BASE_URL);
            var telemetryClient = new TelemetryClient(new TelemetryConfiguration("instrumentationkey"));

            credentials ??= new GraphCredentials
            {
                AuthenticationType = authenticationType,
                ClientId = ClientAppId,
                UserAssignedManagedIdentityClientId = ManagedIdentityAppId
            };

            return new GraphGroupRepository(graphServiceClient.Object,
                                            telemetryClient,
                                            _graphRepositorySettings.Object,
                                            null,
                                            Options.Create(credentials));
        }

        private void SetupServicePrincipalLookup(string servicePrincipalObjectId, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            var value = servicePrincipalObjectId == null
                ? "[]"
                : $"[{{\"id\":\"{servicePrincipalObjectId}\"}}]";

            _requestAdapter.Setup(x => x.SendAsync(It.IsAny<RequestInformation>(),
                                                   It.IsAny<ParsableFactory<ServicePrincipalCollectionResponse>>(),
                                                   It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                                                   It.IsAny<CancellationToken>()))
                           .Callback<object, object, object, object>((request, factory, errorMapping, cancellationToken) =>
                           {
                               var requestInformation = request as RequestInformation;
                               Record(requestInformation);
                               SetResponse(requestInformation, JsonResponse(statusCode, $"{{\"value\":{value}}}"));
                           })
                           .ReturnsAsync(() => servicePrincipalObjectId == null
                                ? new ServicePrincipalCollectionResponse { Value = new List<ServicePrincipal>() }
                                : new ServicePrincipalCollectionResponse
                                {
                                    Value = new List<ServicePrincipal> { new ServicePrincipal { Id = servicePrincipalObjectId } }
                                });
        }

        private void Record(RequestInformation requestInformation)
        {
            _requests.Add(new RecordedRequest
            {
                Method = requestInformation.HttpMethod.ToString(),
                Uri = requestInformation.URI.ToString(),
                Body = ReadContent(requestInformation)
            });
        }

        private static string ReadContent(RequestInformation requestInformation)
        {
            if (requestInformation.Content == null)
            {
                return null;
            }

            if (requestInformation.Content.CanSeek)
            {
                requestInformation.Content.Position = 0;
            }

            var buffer = new byte[requestInformation.Content.Length];
            var index = 0;
            int byteContent;

            while ((byteContent = requestInformation.Content.ReadByte()) != -1)
            {
                buffer[index++] = (byte)byteContent;
            }

            return Encoding.UTF8.GetString(buffer);
        }

        private void RespondWithGroup(RequestInformation requestInformation, HttpStatusCode statusCode)
        {
            SetResponse(requestInformation, JsonResponse(statusCode, $"{{\"id\":\"{_createdGroupId}\",\"displayName\":\"Finance Team\"}}"));
        }

        private static void SetResponse(RequestInformation requestInformation, HttpResponseMessage response)
        {
            var responseHandlerOption = requestInformation.RequestOptions
                                                          .OfType<ResponseHandlerOption>()
                                                          .FirstOrDefault();

            if (responseHandlerOption?.ResponseHandler is NativeResponseHandler nativeResponseHandler)
            {
                nativeResponseHandler.Value = response;
            }
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string content)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content)
            };

            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            return response;
        }

        private JsonNode GetCreateRequestBody()
        {
            var createRequest = _requests.FirstOrDefault(x => x.Method == "POST"
                                                              && x.Uri.TrimEnd('/').EndsWith("/groups", StringComparison.OrdinalIgnoreCase));

            Assert.IsNotNull(createRequest, "Expected a POST to /groups.");

            return JsonNode.Parse(createRequest.Body);
        }

        private List<string> GetOwnerBindings()
        {
            var body = GetCreateRequestBody();
            var bindings = body["owners@odata.bind"];

            Assert.IsNotNull(bindings, "Owners must be bound through owners@odata.bind on the create request.");

            return bindings.AsArray().Select(x => x.GetValue<string>()).ToList();
        }
    }
}
