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

        private static CopilotChatRequestDto ValidRequest(string message = "Include all employees") => new()
        {
            Message = message,
            WorkingQuery = new List<SourcePartDto>()
        };

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
            var request = new CopilotChatRequestDto { Message = "", WorkingQuery = new List<SourcePartDto>() };

            var result = await _controller.ChatAsync(request);

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
        }

        [TestMethod]
        public async Task ChatAsync_WithWhitespaceMessage_ReturnsBadRequest()
        {
            var request = new CopilotChatRequestDto { Message = "   ", WorkingQuery = new List<SourcePartDto>() };

            var result = await _controller.ChatAsync(request);

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
        }

        [TestMethod]
        public async Task ChatAsync_WithNullWorkingQuery_ReturnsBadRequest()
        {
            // A missing workingQuery is a clear InvalidRequest — never a false "loaded" state.
            var request = new CopilotChatRequestDto { Message = "Refine my query", WorkingQuery = null };

            var result = await _controller.ChatAsync(request);

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            _mockHandler.Verify(
                x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()),
                Times.Never);
        }

        [TestMethod]
        public async Task ChatAsync_WithEmptyWorkingQuery_IsAcceptedAsNewQuery()
        {
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse { StatusCode = HttpStatusCode.OK, ResponseMessage = "OK" });

            // Act
            await _controller.ChatAsync(ValidRequest());

            // Assert — an empty (but non-null) workingQuery is a valid new-query request.
            _mockHandler.Verify(x => x.ExecuteAsync(
                It.Is<CopilotChatRequest>(r => r.WorkingQuery != null && r.WorkingQuery.Count == 0)),
                Times.Once);
        }

        [TestMethod]
        public async Task ChatAsync_WithValidRequest_ReturnsOkWithResultingQuery()
        {
            var handlerResponse = new CopilotChatResponse
            {
                StatusCode = HttpStatusCode.OK,
                ResponseMessage = "Here's your query.",
                ResultingQuery = new List<CopilotSourcePartResult>
                {
                    new CopilotSourcePartResult
                    {
                        PartId = "p1",
                        Filter = "category_code = 'value1'",
                        Title = "Sample Title",
                        IsExclusion = false,
                        UseOrgStructure = false
                    }
                },
                AppliedOperations = new List<CopilotOperationSummary>
                {
                    new CopilotOperationSummary { Op = "add", PartId = "p1" }
                }
            };

            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(handlerResponse);

            var result = await _controller.ChatAsync(ValidRequest());

            var okResult = result as OkObjectResult;
            Assert.IsNotNull(okResult);
            var dto = okResult.Value as CopilotChatResponseDto;
            Assert.IsNotNull(dto);
            Assert.AreEqual("Here's your query.", dto.Message);
            Assert.AreEqual(1, dto.ResultingQuery.Count);
            Assert.AreEqual("category_code = 'value1'", dto.ResultingQuery[0].Filter);
            Assert.AreEqual("Sample Title", dto.ResultingQuery[0].Title);
            Assert.AreEqual(1, dto.AppliedOperations.Count);
            Assert.AreEqual("add", dto.AppliedOperations[0].Op);
            Assert.AreEqual("p1", dto.AppliedOperations[0].PartId);
        }

        [TestMethod]
        public async Task ChatAsync_MapsConversationHistoryCorrectly()
        {
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse { StatusCode = HttpStatusCode.OK, ResponseMessage = "OK" });

            var request = new CopilotChatRequestDto
            {
                Message = "Company-wide",
                WorkingQuery = new List<SourcePartDto>(),
                ConversationHistory = new List<ChatMessageDto>
                {
                    new ChatMessageDto { Role = "user", Content = "Include employees" },
                    new ChatMessageDto { Role = "assistant", Content = "Company-wide or org?" }
                }
            };

            await _controller.ChatAsync(request);

            _mockHandler.Verify(x => x.ExecuteAsync(
                It.Is<CopilotChatRequest>(r =>
                    r.UserMessage == "Company-wide" &&
                    r.ConversationHistory.Count == 2 &&
                    r.ConversationHistory[0].Role == "user" &&
                    r.ConversationHistory[1].Content == "Company-wide or org?")),
                Times.Once);
        }

        [TestMethod]
        public async Task ChatAsync_MapsUserContextAndWorkingQueryCorrectly()
        {
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse { StatusCode = HttpStatusCode.OK, ResponseMessage = "OK" });

            var request = new CopilotChatRequestDto
            {
                Message = "My team employees",
                UserContext = new UserContextDto
                {
                    ManagerName = "Jane Smith",
                    ManagerEmail = "jsmith@contoso.com",
                    ManagerAlias = "jsmith"
                },
                WorkingQuery = new List<SourcePartDto>
                {
                    new SourcePartDto { PartId = "w1", Filter = "category_code = 'value1'" }
                }
            };

            await _controller.ChatAsync(request);

            _mockHandler.Verify(x => x.ExecuteAsync(
                It.Is<CopilotChatRequest>(r =>
                    r.UserContext != null &&
                    r.UserContext.ManagerName == "Jane Smith" &&
                    r.UserContext.ManagerEmail == "jsmith@contoso.com" &&
                    r.WorkingQuery != null &&
                    r.WorkingQuery.Count == 1 &&
                    r.WorkingQuery[0].PartId == "w1" &&
                    r.WorkingQuery[0].Filter == "category_code = 'value1'")),
                Times.Once);
        }

        [TestMethod]
        public async Task ChatAsync_WithNullConversationHistory_DefaultsToEmptyList()
        {
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse { StatusCode = HttpStatusCode.OK, ResponseMessage = "OK" });

            var request = new CopilotChatRequestDto
            {
                Message = "Include employees",
                WorkingQuery = new List<SourcePartDto>(),
                ConversationHistory = null
            };

            await _controller.ChatAsync(request);

            _mockHandler.Verify(x => x.ExecuteAsync(
                It.Is<CopilotChatRequest>(r => r.ConversationHistory.Count == 0)),
                Times.Once);
        }

        [TestMethod]
        public async Task ChatAsync_WhenHandlerReturnsBadRequest_ReturnsBadRequest()
        {
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse
                {
                    StatusCode = HttpStatusCode.BadRequest,
                    ResponseMessage = "Invalid input",
                    ErrorCode = "InvalidRequest"
                });

            var result = await _controller.ChatAsync(ValidRequest("test"));

            var badResult = result as BadRequestObjectResult;
            Assert.IsNotNull(badResult);
        }

        [TestMethod]
        public async Task ChatAsync_WhenHandlerReturnsTimeout_Returns408()
        {
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse
                {
                    StatusCode = HttpStatusCode.RequestTimeout,
                    ResponseMessage = "Timed out",
                    ErrorCode = "Timeout"
                });

            var result = await _controller.ChatAsync(ValidRequest("test"));

            var statusResult = result as ObjectResult;
            Assert.IsNotNull(statusResult);
            Assert.AreEqual(StatusCodes.Status408RequestTimeout, statusResult.StatusCode);
        }

        [TestMethod]
        public async Task ChatAsync_WhenHandlerReturnsInternalError_Returns500()
        {
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    ResponseMessage = "Something went wrong",
                    ErrorCode = "InternalError"
                });

            var result = await _controller.ChatAsync(ValidRequest("test"));

            var statusResult = result as ObjectResult;
            Assert.IsNotNull(statusResult);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
        }

        [TestMethod]
        public async Task ChatAsync_MapsOrgLeaderFieldsInResultingQuery()
        {
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse
                {
                    StatusCode = HttpStatusCode.OK,
                    ResponseMessage = "Org filter done.",
                    ResultingQuery = new List<CopilotSourcePartResult>
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

            var result = await _controller.ChatAsync(ValidRequest("Everyone under Jane")) as OkObjectResult;

            Assert.IsNotNull(result);
            var dto = result.Value as CopilotChatResponseDto;
            Assert.IsNotNull(dto);
            var sp = dto.ResultingQuery[0];
            Assert.IsTrue(sp.UseOrgStructure);
            Assert.AreEqual("Jane Smith", sp.OrgLeaderName);
            Assert.AreEqual("jsmith@contoso.com", sp.OrgLeaderEmail);
            Assert.AreEqual("abc-123", sp.OrgLeaderObjectId);
            Assert.AreEqual(3, sp.OrgLeaderDepth);
        }

        [TestMethod]
        public async Task ChatAsync_WithValidConversationId_PassesItThrough()
        {
            var validGuid = "a0d376c9-721f-439a-8952-8a965cb9d2e5";
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse { StatusCode = HttpStatusCode.OK, ResponseMessage = "OK" });

            var request = new CopilotChatRequestDto
            {
                Message = "Include employees",
                WorkingQuery = new List<SourcePartDto>(),
                ConversationId = validGuid
            };

            await _controller.ChatAsync(request);

            _mockHandler.Verify(x => x.ExecuteAsync(
                It.Is<CopilotChatRequest>(r => r.ConversationId == validGuid)),
                Times.Once);
        }

        [TestMethod]
        public async Task ChatAsync_WithInvalidConversationId_SanitizesToNull()
        {
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse { StatusCode = HttpStatusCode.OK, ResponseMessage = "OK" });

            var request = new CopilotChatRequestDto
            {
                Message = "Include employees",
                WorkingQuery = new List<SourcePartDto>(),
                ConversationId = "not-a-guid-at-all"
            };

            await _controller.ChatAsync(request);

            _mockHandler.Verify(x => x.ExecuteAsync(
                It.Is<CopilotChatRequest>(r => r.ConversationId == null)),
                Times.Once);
        }

        [TestMethod]
        public async Task ChatAsync_WithNullConversationId_PassesNull()
        {
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse { StatusCode = HttpStatusCode.OK, ResponseMessage = "OK" });

            var request = new CopilotChatRequestDto
            {
                Message = "Include employees",
                WorkingQuery = new List<SourcePartDto>(),
                ConversationId = null
            };

            await _controller.ChatAsync(request);

            _mockHandler.Verify(x => x.ExecuteAsync(
                It.Is<CopilotChatRequest>(r => r.ConversationId == null)),
                Times.Once);
        }

        [TestMethod]
        public void Constructor_WithNullHandler_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(
                () => new CopilotController(null!));
        }
    }
}
