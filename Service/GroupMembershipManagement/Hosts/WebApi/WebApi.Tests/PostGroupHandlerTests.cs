// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Models;
using Moq;
using Repositories.Contracts;
using Services;
using Services.Messages.Requests;
using WebApi.Tests.ExceptionHandling;

namespace WebApi.Tests
{
    [TestClass]
    public class PostGroupHandlerTests
    {
        private const string AuditMessagePrefix = "Group creation attempt.";

        [TestMethod]
        public async Task ExecuteAsync_WhenCreateGroupThrows_SanitizesResponseDataAndLogsException()
        {
            var loggerMock = new Mock<ILogger<PostGroupHandler>>();
            loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);

            var graphRepoMock = new Mock<IGraphGroupRepository>();
            var thrown = new UnauthorizedAccessException("a distinctive test message");
            graphRepoMock
                .Setup(r => r.CreateGroupFromUI(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ThrowsAsync(thrown);

            var handler = new PostGroupHandler(loggerMock.Object, graphRepoMock.Object);
            var request = new PostGroupRequest(Guid.NewGuid(), "TestGroup", "test-alias");

            var response = await handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.AreEqual("GroupCreationException", response.ErrorCode);
            Assert.IsNotNull(response.ResponseData, "ResponseData must not be null on failure.");
            CollectionAssert.AreEqual(
                new List<string> { "Group creation failed." },
                response.ResponseData,
                "ResponseData must equal the canned sanitized list, not the exception Message.");

            var responseDataJson = JsonSerializer.Serialize(response.ResponseData);
            AssertNoExceptionLeak.Assert(responseDataJson, thrown);

            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    thrown,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once,
                "Expected exactly one Error log carrying the thrown exception.");

            AssertAuditEntry(loggerMock, LogLevel.Error, request.UserIdentity, "TestGroup", "Failed", "GroupCreationException");
        }

        [TestMethod]
        public async Task ExecuteAsync_WhenCreateGroupSucceeds_LogsOneAuditEntry()
        {
            var loggerMock = CreateLogger();
            var graphRepoMock = new Mock<IGraphGroupRepository>();
            var createdGroupId = Guid.NewGuid();

            graphRepoMock
                .Setup(r => r.CreateGroupFromUI(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ReturnsAsync(new AzureADGroup { ObjectId = createdGroupId });

            var handler = new PostGroupHandler(loggerMock.Object, graphRepoMock.Object);
            var request = new PostGroupRequest(Guid.NewGuid(), "TestGroup", "test-alias");

            var response = await handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(createdGroupId, response.GroupId);

            AssertAuditEntry(loggerMock, LogLevel.Information, request.UserIdentity, "TestGroup", "Succeeded", createdGroupId.ToString());
            VerifyLogCount(loggerMock, LogLevel.Error, Times.Never());
        }

        [TestMethod]
        public async Task ExecuteAsync_WhenCreateGroupReturnsNull_LogsOneFailureAuditEntry()
        {
            var loggerMock = CreateLogger();
            var graphRepoMock = new Mock<IGraphGroupRepository>();

            graphRepoMock
                .Setup(r => r.CreateGroupFromUI(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ReturnsAsync((AzureADGroup)null);

            var handler = new PostGroupHandler(loggerMock.Object, graphRepoMock.Object);
            var request = new PostGroupRequest(Guid.NewGuid(), "TestGroup", "test-alias");

            var response = await handler.ExecuteAsync(request);

            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.AreEqual("GroupCreationFailed", response.ErrorCode);

            AssertAuditEntry(loggerMock, LogLevel.Error, request.UserIdentity, "TestGroup", "Failed", "GroupCreationFailed");
            VerifyLogCount(loggerMock, LogLevel.Error, Times.Once());
        }

        [TestMethod]
        public async Task ExecuteAsync_FailureAuditEntryRecordsTheLookupOwnerSource()
        {
            var loggerMock = CreateLogger();
            var graphRepoMock = new Mock<IGraphGroupRepository>();

            graphRepoMock
                .Setup(r => r.CreateGroupFromUI(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
                .ReturnsAsync((AzureADGroup)null);

            var handler = new PostGroupHandler(loggerMock.Object, graphRepoMock.Object);

            await handler.ExecuteAsync(new PostGroupRequest(Guid.NewGuid(), "TestGroup", "test-alias"));

            var entry = GetLoggedMessages(loggerMock, LogLevel.Error).Single();
            StringAssert.Contains(entry, "OwnerSource=ServicePrincipalLookup");
        }

        private static Mock<ILogger<PostGroupHandler>> CreateLogger()
        {
            var loggerMock = new Mock<ILogger<PostGroupHandler>>();
            loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            return loggerMock;
        }

        private static List<string> GetLoggedMessages(Mock<ILogger<PostGroupHandler>> loggerMock, LogLevel level)
        {
            return loggerMock.Invocations
                             .Where(i => i.Method.Name == nameof(ILogger.Log) && (LogLevel)i.Arguments[0] == level)
                             .Select(i => i.Arguments[2].ToString())
                             .Where(m => m.StartsWith(AuditMessagePrefix, StringComparison.Ordinal))
                             .ToList();
        }

        private static void VerifyLogCount(Mock<ILogger<PostGroupHandler>> loggerMock, LogLevel level, Times times)
        {
            loggerMock.Verify(
                l => l.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception?>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                times);
        }

        private static void AssertAuditEntry(Mock<ILogger<PostGroupHandler>> loggerMock,
                                             LogLevel level,
                                             Guid requesterObjectId,
                                             string groupName,
                                             params string[] expectedFragments)
        {
            var messages = GetLoggedMessages(loggerMock, level);

            Assert.AreEqual(1, messages.Count, $"Expected exactly one {level} audit entry.");

            var message = messages.Single();
            StringAssert.Contains(message, requesterObjectId.ToString());
            StringAssert.Contains(message, groupName);

            foreach (var fragment in expectedFragments)
            {
                StringAssert.Contains(message, fragment);
            }
        }
    }
}
