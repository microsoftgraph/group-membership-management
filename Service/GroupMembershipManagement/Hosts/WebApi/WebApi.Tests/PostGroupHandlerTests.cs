// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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
        }
    }
}
