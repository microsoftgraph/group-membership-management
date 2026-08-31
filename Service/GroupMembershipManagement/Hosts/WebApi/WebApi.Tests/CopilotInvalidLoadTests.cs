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
    /// <summary>
    /// US3 (T033): a failed / invalid query load must never silently look like a loaded query.
    /// The controller rejects a null request, an empty message, or a null workingQuery with
    /// 400 InvalidRequest and NEVER calls the handler. An EMPTY workingQuery array is valid — the
    /// user can still start a brand-new query after a load failure.
    /// </summary>
    [TestClass]
    public class CopilotInvalidLoadTests
    {
        private Mock<IRequestHandler<CopilotChatRequest, CopilotChatResponse>> _mockHandler = null!;
        private CopilotController _controller = null!;

        [TestInitialize]
        public void Initialize()
        {
            _mockHandler = new Mock<IRequestHandler<CopilotChatRequest, CopilotChatResponse>>();
            _controller = new CopilotController(_mockHandler.Object)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
        }

        [TestMethod]
        public async Task ChatAsync_NullRequest_ReturnsBadRequestAndSkipsHandler()
        {
            var result = await _controller.ChatAsync(null!);

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            _mockHandler.Verify(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()), Times.Never);
        }

        [TestMethod]
        public async Task ChatAsync_NullWorkingQuery_ReturnsInvalidRequestAndSkipsHandler()
        {
            var request = new CopilotChatRequestDto { Message = "add engineers", WorkingQuery = null };

            var result = await _controller.ChatAsync(request);

            var bad = result as BadRequestObjectResult;
            Assert.IsNotNull(bad, "A null workingQuery must be a 400.");
            StringAssert.Contains(bad!.Value!.ToString(), "InvalidRequest");
            _mockHandler.Verify(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()), Times.Never);
        }

        [TestMethod]
        public async Task ChatAsync_EmptyMessage_ReturnsInvalidRequestAndSkipsHandler()
        {
            var request = new CopilotChatRequestDto { Message = "   ", WorkingQuery = new List<SourcePartDto>() };

            var result = await _controller.ChatAsync(request);

            var bad = result as BadRequestObjectResult;
            Assert.IsNotNull(bad);
            StringAssert.Contains(bad!.Value!.ToString(), "InvalidRequest");
            _mockHandler.Verify(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()), Times.Never);
        }

        [TestMethod]
        public async Task ChatAsync_EmptyWorkingQuery_IsValidAndReachesHandler()
        {
            // A failed load leaves the UI with an empty query — the user must still be able to
            // create a NEW query. An empty array is valid input and must reach the handler.
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()))
                .ReturnsAsync(new CopilotChatResponse
                {
                    StatusCode = HttpStatusCode.OK,
                    ResponseMessage = "Sure, let's build a new query.",
                    ResultingQuery = new List<CopilotSourcePartResult>(),
                    AppliedOperations = new List<CopilotOperationSummary>()
                });

            var request = new CopilotChatRequestDto
            {
                Message = "start a new query for US engineers",
                WorkingQuery = new List<SourcePartDto>()
            };

            var result = await _controller.ChatAsync(request);

            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            _mockHandler.Verify(x => x.ExecuteAsync(It.IsAny<CopilotChatRequest>()), Times.Once);
        }
    }
}
