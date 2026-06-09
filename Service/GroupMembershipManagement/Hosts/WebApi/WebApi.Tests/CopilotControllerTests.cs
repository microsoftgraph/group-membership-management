// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using System.Net;
using WebApi.Controllers.v1.Copilot;

namespace WebApi.Tests
{
    [TestClass]
    public class CopilotControllerTests
    {
        private Mock<IRequestHandler<CopilotChatRequest, CopilotChatResponse>> _mockHandler = null!;
        private CopilotController _controller = null!;

        [TestInitialize]
        public void Initialize()
        {
            _mockHandler = new Mock<IRequestHandler<CopilotChatRequest, CopilotChatResponse>>();
            _controller = new CopilotController(_mockHandler.Object);
            _controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            };
        }

        [TestMethod]
        public async Task ChatAsync_WithNullRequest_ReturnsBadRequest()
        {
            // Act
            var result = await _controller.ChatAsync(null!);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            _mockHandler.Verify(
                x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ChatAsync_WithEmptyMessage_ReturnsBadRequest()
        {
            // Arrange
            var request = new CopilotChatRequestDto { Message = "" };

            // Act
            var result = await _controller.ChatAsync(request);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
        }

        [TestMethod]
        public async Task ChatAsync_WithWhitespaceMessage_ReturnsBadRequest()
        {
            // Arrange
            var request = new CopilotChatRequestDto { Message = "   " };

            // Act
            var result = await _controller.ChatAsync(request);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
        }

        [TestMethod]
        public async Task ChatAsync_WithValidRequest_ReturnsOkWithResponse()
        {
            // Arrange
            var handlerResponse = new CopilotChatResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseMessage = "Here's your filter.",
                SourceParts = new List<CopilotSourcePartResult>
                {
                    new CopilotSourcePartResult
                    {
                        PartId = "p1",
                        Filter = "category_code = 'value1'",
                        Title = "Sample Title",
                        IsExclusion = false,
                        UseOrgStructure = false
                    }
                }
            };

            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(handlerResponse);

            var request = new CopilotChatRequestDto { Message = "Include all employees" };

            // Act
            var result = await _controller.ChatAsync(request);

            // Assert
            var okResult = result as OkObjectResult;
            Assert.IsNotNull(okResult);
            var dto = okResult.Value as CopilotChatResponseDto;
            Assert.IsNotNull(dto);
            Assert.AreEqual("Here's your filter.", dto.Message);
            Assert.AreEqual(1, dto.SourceParts.Count);
            Assert.AreEqual("category_code = 'value1'", dto.SourceParts[0].Filter);
            Assert.AreEqual("Sample Title", dto.SourceParts[0].Title);
        }

        [TestMethod]
        public async Task ChatAsync_MapsConversationHistoryCorrectly()
        {
            // Arrange
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse
                {
                    StatusCode = HttpStatusCode.OK,
                    ResponseMessage = "OK"
                });

            var request = new CopilotChatRequestDto
            {
                Message = "Company-wide",
                ConversationHistory = new List<ChatMessageDto>
                {
                    new ChatMessageDto { Role = "user", Content = "Include employees" },
                    new ChatMessageDto { Role = "assistant", Content = "Company-wide or org?" }
                }
            };

            // Act
            await _controller.ChatAsync(request);

            // Assert
            _mockHandler.Verify(x => x.ExecuteAsync(
                It.Is<CopilotChatRequest>(r =>
                    r.UserMessage == "Company-wide" &&
                    r.ConversationHistory.Count == 2 &&
                    r.ConversationHistory[0].Role == "user" &&
                    r.ConversationHistory[1].Content == "Company-wide or org?")),
                Times.Once);
        }

        [TestMethod]
        public async Task ChatAsync_MapsUserContextCorrectly()
        {
            // Arrange
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse
                {
                    StatusCode = HttpStatusCode.OK,
                    ResponseMessage = "OK"
                });

            var request = new CopilotChatRequestDto
            {
                Message = "My team employees",
                UserContext = new UserContextDto
                {
                    ManagerName = "Jane Smith",
                    ManagerEmail = "jsmith@contoso.com",
                    ManagerAlias = "jsmith"
                },
                CurrentFilter = "category_code = 'value1'"
            };

            // Act
            await _controller.ChatAsync(request);

            // Assert
            _mockHandler.Verify(x => x.ExecuteAsync(
                It.Is<CopilotChatRequest>(r =>
                    r.UserContext != null &&
                    r.UserContext.ManagerName == "Jane Smith" &&
                    r.UserContext.ManagerEmail == "jsmith@contoso.com" &&
                    r.CurrentFilter == "category_code = 'value1'")),
                Times.Once);
        }

        [TestMethod]
        public async Task ChatAsync_WithNullConversationHistory_DefaultsToEmptyList()
        {
            // Arrange
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse
                {
                    StatusCode = HttpStatusCode.OK,
                    ResponseMessage = "OK"
                });

            var request = new CopilotChatRequestDto
            {
                Message = "Include employees",
                ConversationHistory = null
            };

            // Act
            await _controller.ChatAsync(request);

            // Assert
            _mockHandler.Verify(x => x.ExecuteAsync(
                It.Is<CopilotChatRequest>(r => r.ConversationHistory.Count == 0)),
                Times.Once);
        }

        [TestMethod]
        public async Task ChatAsync_WhenHandlerReturnsBadRequest_ReturnsBadRequest()
        {
            // Arrange
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ResponseMessage = "Invalid input",
                    ErrorCode = "InvalidRequest"
                });

            var request = new CopilotChatRequestDto { Message = "test" };

            // Act
            var result = await _controller.ChatAsync(request);

            // Assert
            var badResult = result as BadRequestObjectResult;
            Assert.IsNotNull(badResult);
        }

        [TestMethod]
        public async Task ChatAsync_WhenHandlerReturnsTimeout_Returns408()
        {
            // Arrange
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse
                {
                    StatusCode = HttpStatusCode.RequestTimeout,
                    ResponseMessage = "Timed out",
                    ErrorCode = "Timeout"
                });

            var request = new CopilotChatRequestDto { Message = "test" };

            // Act
            var result = await _controller.ChatAsync(request);

            // Assert
            var statusResult = result as ObjectResult;
            Assert.IsNotNull(statusResult);
            Assert.AreEqual(StatusCodes.Status408RequestTimeout, statusResult.StatusCode);
        }

        [TestMethod]
        public async Task ChatAsync_WhenHandlerReturnsInternalError_Returns500()
        {
            // Arrange
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ResponseMessage = "Something went wrong",
                    ErrorCode = "InternalError"
                });

            var request = new CopilotChatRequestDto { Message = "test" };

            // Act
            var result = await _controller.ChatAsync(request);

            // Assert
            var statusResult = result as ObjectResult;
            Assert.IsNotNull(statusResult);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
        }

        [TestMethod]
        public async Task ChatAsync_MapsOrgLeaderFieldsInResponse()
        {
            // Arrange
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse
                {
                    StatusCode = HttpStatusCode.OK,
                    ResponseMessage = "Org filter done.",
                    SourceParts = new List<CopilotSourcePartResult>
                    {
                        new CopilotSourcePartResult
                        {
                            PartId = "p1",
                            Filter = "",
                            Title = "Jane's Org",
                            UseOrgStructure = true,
                            OrgLeaderName = "Jane Smith",
                            OrgLeaderEmail = "jsmith@contoso.com",
                            OrgLeaderObjectId = "abc-123",
                            OrgLeaderDepth = 3
                        }
                    }
                });

            var request = new CopilotChatRequestDto { Message = "Everyone under Jane" };

            // Act
            var result = await _controller.ChatAsync(request) as OkObjectResult;

            // Assert
            Assert.IsNotNull(result);
            var dto = result.Value as CopilotChatResponseDto;
            Assert.IsNotNull(dto);
            var sp = dto.SourceParts[0];
            Assert.IsTrue(sp.UseOrgStructure);
            Assert.AreEqual("Jane Smith", sp.OrgLeaderName);
            Assert.AreEqual("jsmith@contoso.com", sp.OrgLeaderEmail);
            Assert.AreEqual("abc-123", sp.OrgLeaderObjectId);
            Assert.AreEqual(3, sp.OrgLeaderDepth);
        }

        [TestMethod]
        public void Constructor_WithNullHandler_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => new CopilotController(null!));
        }
    }
}
