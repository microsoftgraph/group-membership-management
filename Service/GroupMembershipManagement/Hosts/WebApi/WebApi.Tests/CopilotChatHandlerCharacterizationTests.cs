// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Services.Messages.Requests;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Net;

namespace WebApi.Tests
{
    /// <summary>
    /// Characterization tests pinning the operation-engine behavior of <see cref="CopilotChatHandler"/>:
    /// resulting-query / applied-operations / warning passthrough, atomic-reject mapping, and the
    /// threading of the inbound workingQuery to the service. These lock in behavior the v2 endpoint depends on.
    /// </summary>
    [TestClass]
    public class CopilotChatHandlerCharacterizationTests
    {
        private Mock<ICopilotService> _mockCopilotService = null!;
        private CopilotChatHandler _handler = null!;

        [TestInitialize]
        public void Initialize()
        {
            _mockCopilotService = new Mock<ICopilotService>();
            _handler = new CopilotChatHandler(NullLogger<CopilotChatHandler>.Instance, _mockCopilotService.Object);
        }

        private void SetupService(CopilotChatResult result) =>
            _mockCopilotService
                .Setup(x => x.GetChatResponseAsync(
                    It.IsAny<string>(),
                    It.IsAny<List<CopilotChatMessage>>(),
                    It.IsAny<CopilotUserContext?>(),
                    It.IsAny<List<CopilotSourcePartResult>?>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(result);

        [TestMethod]
        public async Task ExecuteAsync_PassesResultingQueryAndAppliedOperations()
        {
            var result = new CopilotChatResult
            {
                ResponseMessage = "Added a part.",
                ResultingQuery = new List<CopilotSourcePartResult>
                {
                    new() { PartId = "p1", Filter = "Country = 'USA'", Title = "US" }
                },
                AppliedOperations = new List<CopilotOperationSummary>
                {
                    new() { Op = "add", PartId = "p1" }
                }
            };
            SetupService(result);

            var response = await _handler.ExecuteAsync(
                new CopilotChatRequest("add US", new List<CopilotChatMessage>()));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(1, response.ResultingQuery.Count);
            Assert.AreEqual("p1", response.ResultingQuery[0].PartId);
            Assert.AreEqual(1, response.AppliedOperations.Count);
            Assert.AreEqual("add", response.AppliedOperations[0].Op);
        }

        [TestMethod]
        public async Task ExecuteAsync_PassesWarningWhenResultingQueryEmpty()
        {
            SetupService(new CopilotChatResult
            {
                ResponseMessage = "Removed the only part.",
                ResultingQuery = new List<CopilotSourcePartResult>(),
                Warning = "This query now has no membership criteria."
            });

            var response = await _handler.ExecuteAsync(
                new CopilotChatRequest("remove it", new List<CopilotChatMessage>()));

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("This query now has no membership criteria.", response.Warning);
        }

        [TestMethod]
        public async Task ExecuteAsync_AtomicReject_MapsToBadRequestWithUnchangedQuery()
        {
            var unchanged = new List<CopilotSourcePartResult>
            {
                new() { PartId = "keep-me", Filter = "Country = 'USA'", Title = "US" }
            };
            SetupService(new CopilotChatResult
            {
                ResponseMessage = "That part is not in your query.",
                ErrorCode = "UnknownPartTarget",
                ResultingQuery = unchanged
            });

            var response = await _handler.ExecuteAsync(
                new CopilotChatRequest("remove ghost", new List<CopilotChatMessage>()));

            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("UnknownPartTarget", response.ErrorCode);
            Assert.AreEqual(1, response.ResultingQuery.Count);
            Assert.AreEqual("keep-me", response.ResultingQuery[0].PartId);
        }

        [TestMethod]
        public async Task ExecuteAsync_ThreadsWorkingQueryToService()
        {
            SetupService(new CopilotChatResult { ResponseMessage = "ok" });

            var workingQuery = new List<CopilotSourcePartResult>
            {
                new() { PartId = "w1", SourceType = "SqlMembership", Filter = "A = 1", Title = "A" },
                new() { PartId = "w2", SourceType = "GroupOwnership", Title = "Owners" }
            };
            var request = new CopilotChatRequest(
                "refine", new List<CopilotChatMessage>(), null, "conv-1", workingQuery);

            await _handler.ExecuteAsync(request);

            _mockCopilotService.Verify(
                x => x.GetChatResponseAsync(
                    "refine",
                    It.IsAny<List<CopilotChatMessage>>(),
                    It.IsAny<CopilotUserContext?>(),
                    It.Is<List<CopilotSourcePartResult>?>(w => w != null && w.Count == 2 && w[1].PartId == "w2"),
                    "conv-1"),
                Times.Once);
        }
    }
}
