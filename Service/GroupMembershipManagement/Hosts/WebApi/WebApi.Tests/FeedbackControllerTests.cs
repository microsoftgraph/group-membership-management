// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;
using System.Reflection;
using WebApi.Controllers.v1.Feedback;

namespace WebApi.Tests
{
    [TestClass]
    public class FeedbackControllerTests
    {
        private const string Feedback = "the exclusions are not clear. please explain why each one is needed";

        private Mock<IRequestHandler<RefineFeedbackRequest, RefineFeedbackResponse>> _mockHandler = null!;
        private FeedbackController _controller = null!;

        [TestInitialize]
        public void Initialize()
        {
            _mockHandler = new Mock<IRequestHandler<RefineFeedbackRequest, RefineFeedbackResponse>>();
            _controller = new FeedbackController(_mockHandler.Object)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext()
                }
            };
        }

        [TestMethod]
        public async Task RefineAsync_WithNullRequest_ReturnsBadRequestAndDoesNotCallHandler()
        {
            var result = await _controller.RefineAsync(null!);

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            _mockHandler.Verify(x => x.ExecuteAsync(It.IsAny<RefineFeedbackRequest>()), Times.Never);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("\t\n ")]
        public async Task RefineAsync_WithEmptyOrWhitespaceFeedback_ReturnsBadRequestAndDoesNotCallHandler(string feedback)
        {
            var result = await _controller.RefineAsync(new RefineFeedbackRequestDto { Feedback = feedback });

            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            _mockHandler.Verify(x => x.ExecuteAsync(It.IsAny<RefineFeedbackRequest>()), Times.Never);
        }

        [TestMethod]
        public async Task RefineAsync_WithSuccessfulRefinement_ReturnsRefinedText()
        {
            SetupHandler(new RefineFeedbackResponse
            {
                StatusCode = HttpStatusCode.OK,
                RefinedText = "Refined."
            });

            var result = await _controller.RefineAsync(new RefineFeedbackRequestDto { Feedback = Feedback });

            var okResult = (OkObjectResult)result;
            var dto = (RefineFeedbackResponseDto)okResult.Value!;
            Assert.AreEqual("Refined.", dto.RefinedText);
        }

        [TestMethod]
        public async Task RefineAsync_PassesExactFeedbackToHandler()
        {
            RefineFeedbackRequest? captured = null;
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<RefineFeedbackRequest>()))
                .Callback<RefineFeedbackRequest>(r => captured = r)
                .ReturnsAsync(new RefineFeedbackResponse { StatusCode = HttpStatusCode.OK, RefinedText = "Refined." });

            await _controller.RefineAsync(new RefineFeedbackRequestDto { Feedback = Feedback });

            Assert.IsNotNull(captured);
            Assert.AreEqual(Feedback, captured!.Feedback);
        }

        [DataTestMethod]
        [DataRow(HttpStatusCode.BadRequest, 400, RefineFeedbackErrorCodes.InvalidRequest)]
        [DataRow(HttpStatusCode.RequestTimeout, 408, RefineFeedbackErrorCodes.Timeout)]
        [DataRow(HttpStatusCode.UnprocessableEntity, 422, RefineFeedbackErrorCodes.InvalidRefinedText)]
        [DataRow(HttpStatusCode.UnprocessableEntity, 422, RefineFeedbackErrorCodes.RefinedTextTooLong)]
        [DataRow(HttpStatusCode.ServiceUnavailable, 503, RefineFeedbackErrorCodes.ServiceUnavailable)]
        [DataRow(HttpStatusCode.ServiceUnavailable, 503, RefineFeedbackErrorCodes.FeatureDisabled)]
        [DataRow(HttpStatusCode.InternalServerError, 500, RefineFeedbackErrorCodes.InternalError)]
        public async Task RefineAsync_MapsHandlerFailuresToStatusCodes(HttpStatusCode handlerStatus, int expectedStatus, string errorCode)
        {
            SetupHandler(new RefineFeedbackResponse
            {
                StatusCode = handlerStatus,
                ErrorCode = errorCode,
                ResponseMessage = "Feedback refinement failed."
            });

            var result = await _controller.RefineAsync(new RefineFeedbackRequestDto { Feedback = Feedback });

            var statusResult = (ObjectResult)result;
            Assert.AreEqual(expectedStatus, statusResult.StatusCode);
            Assert.AreEqual(errorCode, GetProperty(statusResult.Value!, "code"));
        }

        [TestMethod]
        public async Task RefineAsync_OnFailure_DoesNotEchoFeedback()
        {
            SetupHandler(new RefineFeedbackResponse
            {
                StatusCode = HttpStatusCode.ServiceUnavailable,
                ErrorCode = RefineFeedbackErrorCodes.ServiceUnavailable,
                ResponseMessage = "Feedback refinement is unavailable."
            });

            var result = await _controller.RefineAsync(new RefineFeedbackRequestDto { Feedback = Feedback });

            var payload = ((ObjectResult)result).Value!.ToString()!;
            StringAssert.DoesNotMatch(payload, new System.Text.RegularExpressions.Regex("exclusions"));
        }

        [TestMethod]
        public void RefineAsync_IsAuthorizedForTheSameRolesAsRejectionSubmission()
        {
            var method = typeof(FeedbackController).GetMethod(nameof(FeedbackController.RefineAsync))!;
            var authorize = method.GetCustomAttribute<AuthorizeAttribute>();

            Assert.IsNotNull(authorize);

            // Anyone who can submit a rejection must be able to refine its feedback, and no one else.
            var reviewMethod = typeof(WebApi.Controllers.v1.Jobs.JobDetailsController)
                .GetMethod(nameof(WebApi.Controllers.v1.Jobs.JobDetailsController.ReviewJobAsync))!;
            var reviewAuthorize = reviewMethod.GetCustomAttribute<AuthorizeAttribute>();

            CollectionAssert.AreEquivalent(
                SplitRoles(reviewAuthorize!.Roles),
                SplitRoles(authorize!.Roles));
        }

        private static string[] SplitRoles(string? roles) =>
            (roles ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(r => r)
                .ToArray();

        private void SetupHandler(RefineFeedbackResponse response)
        {
            _mockHandler
                .Setup(x => x.ExecuteAsync(It.IsAny<RefineFeedbackRequest>()))
                .ReturnsAsync(response);
        }

        private static string? GetProperty(object value, string propertyName)
        {
            return value.GetType().GetProperty(propertyName)?.GetValue(value)?.ToString();
        }
    }
}
