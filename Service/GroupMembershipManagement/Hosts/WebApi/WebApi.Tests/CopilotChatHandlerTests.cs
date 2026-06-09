// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Moq;
using Repositories.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Net;

namespace WebApi.Tests
{
    [TestClass]
    public class CopilotChatHandlerTests
    {
        private Mock<ICopilotService> _mockCopilotService = null!;
        private Mock<ILoggingRepository> _mockLoggingRepository = null!;
        private CopilotChatHandler _handler = null!;

        [TestInitialize]
        public void Initialize()
        {
            _mockCopilotService = new Mock<ICopilotService>();
            _mockLoggingRepository = new Mock<ILoggingRepository>();
            _handler = new CopilotChatHandler(_mockCopilotService.Object, _mockLoggingRepository.Object);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithValidMessage_ReturnsOkWithResponse()
        {
            // Arrange
            var expectedResult = new CopilotChatResult
            {
                ResponseMessage = "I can help with that!",
                SourceParts = new List<CopilotSourcePartResult>()
            };

            _mockCopilotService
                .Setup(x => x.GetChatResponseAsync(
                    It.IsAny<string>(),
                    It.IsAny<List<CopilotChatMessage>>(),
                    It.IsAny<CopilotUserContext?>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(expectedResult);

            var request = new CopilotChatRequest("Include all employees", new List<CopilotChatMessage>());

            // Act
            var response = await _handler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("I can help with that!", response.ResponseMessage);
            Assert.AreEqual(0, response.SourceParts.Count);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithSourceParts_ReturnsSourceParts()
        {
            // Arrange
            var expectedResult = new CopilotChatResult
            {
                ResponseMessage = "Here's your filter.",
                SourceParts = new List<CopilotSourcePartResult>
                {
                    new CopilotSourcePartResult
                    {
                        PartId = "part-1",
                        Filter = "category_code = 'value1'",
                        Title = "Sample Title",
                        IsExclusion = false,
                        UseOrgStructure = false
                    }
                }
            };

            _mockCopilotService
                .Setup(x => x.GetChatResponseAsync(
                    It.IsAny<string>(),
                    It.IsAny<List<CopilotChatMessage>>(),
                    It.IsAny<CopilotUserContext?>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(expectedResult);

            var request = new CopilotChatRequest("Include all employees", new List<CopilotChatMessage>());

            // Act
            var response = await _handler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(1, response.SourceParts.Count);
            Assert.AreEqual("category_code = 'value1'", response.SourceParts[0].Filter);
            Assert.AreEqual("Sample Title", response.SourceParts[0].Title);
            Assert.IsFalse(response.SourceParts[0].IsExclusion);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithOrgStructure_ReturnsOrgLeaderInfo()
        {
            // Arrange
            var expectedResult = new CopilotChatResult
            {
                ResponseMessage = "Org filter set up.",
                SourceParts = new List<CopilotSourcePartResult>
                {
                    new CopilotSourcePartResult
                    {
                        PartId = "part-1",
                        Filter = "category_code = 'value1'",
                        Title = "Jane's Org",
                        UseOrgStructure = true,
                        OrgLeaderName = "Jane Smith",
                        OrgLeaderEmail = "jsmith@contoso.com",
                        OrgLeaderObjectId = "obj-123",
                        OrgLeaderDepth = 5
                    }
                }
            };

            _mockCopilotService
                .Setup(x => x.GetChatResponseAsync(
                    It.IsAny<string>(),
                    It.IsAny<List<CopilotChatMessage>>(),
                    It.IsAny<CopilotUserContext?>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(expectedResult);

            var request = new CopilotChatRequest("Employees under Jane Smith", new List<CopilotChatMessage>());

            // Act
            var response = await _handler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.IsTrue(response.UseOrgStructure);
            Assert.AreEqual("Jane Smith", response.OrgLeaderName);
            Assert.AreEqual("jsmith@contoso.com", response.OrgLeaderEmail);
            Assert.AreEqual("obj-123", response.OrgLeaderObjectId);
            Assert.AreEqual(5, response.OrgLeaderDepth);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithEmptyMessage_ReturnsBadRequest()
        {
            // Arrange
            var request = new CopilotChatRequest("", new List<CopilotChatMessage>());

            // Act
            var response = await _handler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("InvalidRequest", response.ErrorCode);
            _mockCopilotService.Verify(
                x => x.GetChatResponseAsync(It.IsAny<string>(), It.IsAny<List<CopilotChatMessage>>(), It.IsAny<CopilotUserContext?>(), It.IsAny<string?>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithWhitespaceMessage_ReturnsBadRequest()
        {
            // Arrange
            var request = new CopilotChatRequest("   ", new List<CopilotChatMessage>());

            // Act
            var response = await _handler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.AreEqual("InvalidRequest", response.ErrorCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_WhenServiceTimesOut_ReturnsTimeout()
        {
            // Arrange
            _mockCopilotService
                .Setup(x => x.GetChatResponseAsync(
                    It.IsAny<string>(),
                    It.IsAny<List<CopilotChatMessage>>(),
                    It.IsAny<CopilotUserContext?>(),
                    It.IsAny<string?>()))
                .ThrowsAsync(new TimeoutException("OpenAI API call timed out"));

            var request = new CopilotChatRequest("Include all employees", new List<CopilotChatMessage>());

            // Act
            var response = await _handler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.RequestTimeout, response.StatusCode);
            Assert.AreEqual("Timeout", response.ErrorCode);
            Assert.IsTrue(response.ResponseMessage.Contains("too long"));
        }

        [TestMethod]
        public async Task ExecuteAsync_WhenServiceThrows_ReturnsInternalError()
        {
            // Arrange
            _mockCopilotService
                .Setup(x => x.GetChatResponseAsync(
                    It.IsAny<string>(),
                    It.IsAny<List<CopilotChatMessage>>(),
                    It.IsAny<CopilotUserContext?>(),
                    It.IsAny<string?>()))
                .ThrowsAsync(new InvalidOperationException("Exceeded maximum tool calls"));

            var request = new CopilotChatRequest("Include all employees", new List<CopilotChatMessage>());

            // Act
            var response = await _handler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.AreEqual("InternalError", response.ErrorCode);
        }

        [TestMethod]
        public async Task ExecuteAsync_PassesConversationHistoryToService()
        {
            // Arrange
            var history = new List<CopilotChatMessage>
            {
                new CopilotChatMessage { Role = "user", Content = "Include employees" },
                new CopilotChatMessage { Role = "assistant", Content = "Sure, company-wide or within an org?" }
            };

            _mockCopilotService
                .Setup(x => x.GetChatResponseAsync(
                    "Company-wide",
                    It.Is<List<CopilotChatMessage>>(h => h.Count == 2),
                    It.IsAny<CopilotUserContext?>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(new CopilotChatResult { ResponseMessage = "Done" });

            var request = new CopilotChatRequest("Company-wide", history);

            // Act
            await _handler.ExecuteAsync(request);

            // Assert
            _mockCopilotService.Verify(
                x => x.GetChatResponseAsync(
                    "Company-wide",
                    It.Is<List<CopilotChatMessage>>(h => h.Count == 2 && h[0].Role == "user"),
                    null,
                    null),
                Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_PassesUserContextAndCurrentFilter()
        {
            // Arrange
            var userContext = new CopilotUserContext
            {
                ManagerName = "Jane Smith",
                ManagerEmail = "jsmith@contoso.com"
            };

            _mockCopilotService
                .Setup(x => x.GetChatResponseAsync(
                    It.IsAny<string>(),
                    It.IsAny<List<CopilotChatMessage>>(),
                    It.IsAny<CopilotUserContext?>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(new CopilotChatResult { ResponseMessage = "Got it" });

            var request = new CopilotChatRequest("Include my team", new List<CopilotChatMessage>(), userContext, "category_code = 'value1'");

            // Act
            await _handler.ExecuteAsync(request);

            // Assert
            _mockCopilotService.Verify(
                x => x.GetChatResponseAsync(
                    "Include my team",
                    It.IsAny<List<CopilotChatMessage>>(),
                    It.Is<CopilotUserContext>(uc => uc.ManagerName == "Jane Smith" && uc.ManagerEmail == "jsmith@contoso.com"),
                    "category_code = 'value1'"),
                Times.Once);
        }

        [TestMethod]
        public async Task ExecuteAsync_WithMultipleSourceParts_ReturnsAll()
        {
            // Arrange
            var expectedResult = new CopilotChatResult
            {
                ResponseMessage = "Both orgs set up.",
                SourceParts = new List<CopilotSourcePartResult>
                {
                    new CopilotSourcePartResult
                    {
                        PartId = "part-1",
                        Filter = "category_code = 'value1'",
                        Title = "User 1's Org",
                        UseOrgStructure = true,
                        OrgLeaderName = "User 1",
                        OrgLeaderEmail = "user1@company.com"
                    },
                    new CopilotSourcePartResult
                    {
                        PartId = "part-2",
                        Filter = "category_code = 'value1'",
                        Title = "User 2's Org",
                        UseOrgStructure = true,
                        OrgLeaderName = "User 2",
                        OrgLeaderEmail = "user2@company.com"
                    }
                }
            };

            _mockCopilotService
                .Setup(x => x.GetChatResponseAsync(
                    It.IsAny<string>(),
                    It.IsAny<List<CopilotChatMessage>>(),
                    It.IsAny<CopilotUserContext?>(),
                    It.IsAny<string?>()))
                .ReturnsAsync(expectedResult);

            var request = new CopilotChatRequest("Employees under User 1 and User 2", new List<CopilotChatMessage>());

            // Act
            var response = await _handler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual(2, response.SourceParts.Count);
            Assert.AreEqual("User 1", response.SourceParts[0].OrgLeaderName);
            Assert.AreEqual("User 2", response.SourceParts[1].OrgLeaderName);
        }

        [TestMethod]
        public void Constructor_WithNullCopilotService_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => new CopilotChatHandler(null!, _mockLoggingRepository.Object));
        }

        [TestMethod]
        public void Constructor_WithNullLoggingRepository_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => new CopilotChatHandler(_mockCopilotService.Object, null!));
        }
    }
}
