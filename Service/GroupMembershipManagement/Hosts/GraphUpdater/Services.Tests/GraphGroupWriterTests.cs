// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts;
using Repositories.GraphGroups;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using TestGroupType = Models.TestGroupType;

namespace Services.Tests
{
    [TestClass]
    public class GraphGroupWriterTests
    {
        private Mock<IRequestAdapter> _requestAdapter;
        private GraphGroupRepository _repository;
        private TelemetryConfiguration _telemetryConfiguration;
        private readonly List<RequestInformation> _requests = new();
        private readonly Guid _groupId = Guid.NewGuid();

        [TestInitialize]
        public void Setup()
        {
            _requests.Clear();
            _requestAdapter = new Mock<IRequestAdapter>();
            _requestAdapter.SetupProperty(x => x.BaseUrl, "https://graph.microsoft.com/v1.0");
            _requestAdapter.Setup(x => x.SerializationWriterFactory).Returns(new JsonSerializationWriterFactory());
            _requestAdapter.Setup(x => x.SendAsync(
                    It.IsAny<RequestInformation>(), It.IsAny<ParsableFactory<GroupCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(), It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<GroupCollectionResponse>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, factory, errors, token) => _requests.Add(request))
                .ReturnsAsync(new GroupCollectionResponse { Value = new List<Group>() });
            _requestAdapter.Setup(x => x.SendAsync(
                    It.IsAny<RequestInformation>(), It.IsAny<ParsableFactory<Group>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(), It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, ParsableFactory<Group>, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, factory, errors, token) => _requests.Add(request))
                .ReturnsAsync(new Group { Id = _groupId.ToString() });
            _requestAdapter.Setup(x => x.SendNoContentAsync(
                    It.IsAny<RequestInformation>(), It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, errors, token) => _requests.Add(request))
                .Returns(Task.CompletedTask);

            _telemetryConfiguration = new TelemetryConfiguration { DisableTelemetry = true };
            _repository = new GraphGroupRepository(
                new GraphServiceClient(_requestAdapter.Object),
                new TelemetryClient(_telemetryConfiguration),
                Mock.Of<IRetryPolicyProvider>());
        }

        [TestCleanup]
        public void Cleanup()
        {
            _telemetryConfiguration.Dispose();
        }

        [TestMethod]
        [DataRow(TestGroupType.IntegrationTesting)]
        [DataRow(TestGroupType.LoadTesting)]
        public async Task LegacyCreationPreservesPayloadAndLookup(TestGroupType groupType)
        {
            var result = await _repository.CreateGroup("Test Group", groupType);

            Assert.AreEqual(_groupId, result.ObjectId);
            CollectionAssert.AreEqual(new[] { Method.GET, Method.POST }, _requests.Select(r => r.HttpMethod).ToArray());
            StringAssert.Contains(Uri.UnescapeDataString(_requests[0].URI.Query), "displayName eq 'Test Group'");
            var body = ReadBody(_requests[1]);
            Assert.AreEqual("Test Group", body["displayName"].GetValue<string>());
            Assert.IsFalse(body["mailEnabled"].GetValue<bool>());
            Assert.IsTrue(body["securityEnabled"].GetValue<bool>());
            var nickname = Guid.Parse(body["mailNickname"].GetValue<string>());
            if (groupType == TestGroupType.IntegrationTesting)
            {
                Assert.AreEqual("Integration test group: Test Group", body["description"].GetValue<string>());
                Assert.AreEqual(Guid.Empty, nickname);
                Assert.IsNull(body["groupTypes"]);
            }
            else
            {
                Assert.AreEqual("Load test group: Test Group", body["description"].GetValue<string>());
                Assert.AreNotEqual(Guid.Empty, nickname);
                CollectionAssert.AreEqual(new[] { "Unified" }, body["groupTypes"].AsArray().Select(n => n.GetValue<string>()).ToArray());
            }
        }

        [TestMethod]
        public async Task LegacyCreationReturnsExistingGroupWithoutWriting()
        {
            _requestAdapter.Setup(x => x.SendAsync(
                    It.IsAny<RequestInformation>(), It.IsAny<ParsableFactory<GroupCollectionResponse>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new GroupCollectionResponse
                {
                    Value = new List<Group>
                    {
                        new Group { Id = _groupId.ToString(), DisplayName = "Test Group", Mail = "test@example.com" }
                    }
                });

            var result = await _repository.CreateGroup("Test Group", TestGroupType.IntegrationTesting);

            Assert.AreEqual(_groupId, result.ObjectId);
            Assert.AreEqual("Test Group", result.Name);
            Assert.AreEqual("test@example.com", result.Email);
            _requestAdapter.Verify(x => x.SendAsync(
                It.IsAny<RequestInformation>(), It.IsAny<ParsableFactory<Group>>(),
                It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestMethod]
        public async Task LegacyCreationReturnsNullWithoutACreatedGroup()
        {
            _requestAdapter.Setup(x => x.SendAsync(
                    It.IsAny<RequestInformation>(), It.IsAny<ParsableFactory<Group>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Group)null);

            Assert.IsNull(await _repository.CreateGroup("Test Group", TestGroupType.IntegrationTesting));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task LegacyCreationPreservesErrorHandling(bool graphError)
        {
            var exception = graphError
                ? (Exception)new ODataError { ResponseStatusCode = (int)HttpStatusCode.Forbidden }
                : new InvalidOperationException("Transport failed");
            _requestAdapter.Setup(x => x.SendAsync(
                    It.IsAny<RequestInformation>(), It.IsAny<ParsableFactory<Group>>(),
                    It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            if (graphError)
            {
                var actual = await Assert.ThrowsExceptionAsync<ODataError>(
                    () => _repository.CreateGroup("Test Group", TestGroupType.IntegrationTesting));
                Assert.AreSame(exception, actual);
            }
            else
            {
                Assert.IsNull(await _repository.CreateGroup("Test Group", TestGroupType.IntegrationTesting));
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task OwnerAssignmentDoesNothingWithoutOwners(bool nullOwners)
        {
            await _repository.AddGroupOwners(_groupId.ToString(), nullOwners ? null : new List<Guid>());

            Assert.AreEqual(0, _requests.Count);
        }

        [TestMethod]
        public async Task OwnerAssignmentWritesEachDirectoryObjectReference()
        {
            var owners = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };

            await _repository.AddGroupOwners(_groupId.ToString(), owners);

            Assert.AreEqual(owners.Count, _requests.Count);
            for (var i = 0; i < owners.Count; i++)
            {
                Assert.AreEqual(Method.POST, _requests[i].HttpMethod);
                Assert.AreEqual($"https://graph.microsoft.com/v1.0/groups/{_groupId}/owners/$ref", _requests[i].URI.ToString());
                Assert.AreEqual($"https://graph.microsoft.com/v1.0/directoryObjects/{owners[i]}",
                    ReadBody(_requests[i])["@odata.id"].GetValue<string>());
            }
        }

        [TestMethod]
        public async Task OwnerAssignmentRetriesNotFoundWithoutRecreatingTheGroup()
        {
            _requestAdapter.SetupSequence(x => x.SendNoContentAsync(
                    It.IsAny<RequestInformation>(), It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ODataError { ResponseStatusCode = (int)HttpStatusCode.NotFound })
                .Returns(Task.CompletedTask);

            await _repository.AddGroupOwners(_groupId.ToString(), new List<Guid> { Guid.NewGuid() });

            _requestAdapter.Verify(x => x.SendNoContentAsync(
                It.IsAny<RequestInformation>(), It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                It.IsAny<CancellationToken>()), Times.Exactly(2));
            _requestAdapter.Verify(x => x.SendAsync(
                It.IsAny<RequestInformation>(), It.IsAny<ParsableFactory<Group>>(),
                It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [TestMethod]
        public async Task OwnerAssignmentSkipsAnExistingOwnerAndContinues()
        {
            var owners = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
            var attemptedOwners = new List<string>();
            _requestAdapter.Setup(x => x.SendNoContentAsync(
                    It.IsAny<RequestInformation>(), It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>(
                    (request, errors, token) =>
                    {
                        attemptedOwners.Add(ReadBody(request)["@odata.id"].GetValue<string>());
                        if (attemptedOwners.Count == 1)
                        {
                            throw new ODataError
                            {
                                ResponseStatusCode = (int)HttpStatusCode.BadRequest,
                                Error = new MainError { Message = "One or more added object references already exist." }
                            };
                        }
                    })
                .Returns(Task.CompletedTask);

            await _repository.AddGroupOwners(_groupId.ToString(), owners);

            CollectionAssert.AreEqual(
                owners.Select(id => $"https://graph.microsoft.com/v1.0/directoryObjects/{id}").ToArray(),
                attemptedOwners.ToArray());
        }

        [TestMethod]
        [DataRow(HttpStatusCode.BadRequest)]
        [DataRow(HttpStatusCode.Forbidden)]
        public async Task OwnerAssignmentPropagatesOtherGraphErrors(HttpStatusCode status)
        {
            var exception = new ODataError { ResponseStatusCode = (int)status };
            _requestAdapter.Setup(x => x.SendNoContentAsync(
                    It.IsAny<RequestInformation>(), It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception);

            var actual = await Assert.ThrowsExceptionAsync<ODataError>(
                () => _repository.AddGroupOwners(_groupId.ToString(), new List<Guid> { Guid.NewGuid(), Guid.NewGuid() }));

            Assert.AreSame(exception, actual);
            _requestAdapter.Verify(x => x.SendNoContentAsync(
                It.IsAny<RequestInformation>(), It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        private static JsonNode ReadBody(RequestInformation request)
        {
            request.Content.Position = 0;
            return JsonNode.Parse(request.Content);
        }
    }
}
