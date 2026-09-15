// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Models.Dashboard;
using Moq;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi;
using System.Net;
using System.Security.Claims;
using WebApi.Controllers.v1.Dashboard;
using WebApi.Models;
using WebApi.Tests.ExceptionHandling;

namespace WebApi.Tests
{
    [TestClass]
    public class DashboardControllerTests
    {
        private DashboardController _dashboardController = null!;
        private GetDashboardSummaryHandler _getDashboardSummaryHandler = null!;
        private GetApprovalActivityHandler _getApprovalActivityHandler = null!;
        private Mock<IDatabaseSyncJobsRepository> _databaseSyncJobsRepository = null!;
        private Mock<ILogger<DashboardController>> _logger = null!;

        [TestInitialize]
        public void Initialize()
        {
            _databaseSyncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            _logger = new Mock<ILogger<DashboardController>>();

            _getDashboardSummaryHandler = new GetDashboardSummaryHandler(NullLogger<GetDashboardSummaryHandler>.Instance,
                                                                        _databaseSyncJobsRepository.Object);

            _getApprovalActivityHandler = new GetApprovalActivityHandler(NullLogger<GetApprovalActivityHandler>.Instance,
                                                                        _databaseSyncJobsRepository.Object);

            _dashboardController = new DashboardController(_getDashboardSummaryHandler, _getApprovalActivityHandler, _logger.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Role, Roles.DASHBOARD_READER)
                })
            };
        }

        [TestMethod]
        public async Task GetSummaryAsync_ReturnsAggregatesFromTheRepository()
        {
            var summary = new DashboardSummary
            {
                QueriedAtUtc = new DateTime(2026, 1, 4, 0, 0, 0, DateTimeKind.Utc),
                PendingReview = new List<PendingReviewSummaryRow>
                {
                    new() { RequestType = "Onboarding", Count = 3, OldestChangeTime = new DateTime(2026, 1, 1) }
                },
                InProgress = new List<InProgressStatusRow>
                {
                    new() { Status = "InProgress", Count = 12, OldestStartTime = new DateTime(2026, 1, 2) }
                },
                AnomalousStatuses = new List<AnomalousStatusRow>
                {
                    new() { Status = "Error", Count = 5, MaxCyclesMissed = 4, NeverSucceededCount = 1 }
                }
            };

            _databaseSyncJobsRepository.Setup(x => x.GetDashboardSummaryAsync(It.IsAny<CancellationToken>()))
                                       .ReturnsAsync(summary);

            var response = await _dashboardController.GetSummaryAsync();

            var result = response as OkObjectResult;
            Assert.IsNotNull(result);

            var summaryResponse = result.Value as GetDashboardSummaryResponse;
            Assert.IsNotNull(summaryResponse);
            Assert.AreEqual(HttpStatusCode.OK, summaryResponse.StatusCode);
            Assert.AreEqual(3, summaryResponse.Summary.PendingReview.Single().Count);
            Assert.AreEqual(12, summaryResponse.Summary.InProgress.Single().Count);
            Assert.AreEqual(4, summaryResponse.Summary.AnomalousStatuses.Single().MaxCyclesMissed);
        }

        [TestMethod]
        public async Task GetSummaryAsync_ReturnsInternalServerErrorWhenTheRepositoryFails()
        {
            _databaseSyncJobsRepository.Setup(x => x.GetDashboardSummaryAsync(It.IsAny<CancellationToken>()))
                                       .ThrowsAsync(new Exception("An error occurred"));

            var response = await _dashboardController.GetSummaryAsync();

            var result = response as ObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.InternalServerError, result.StatusCode);
        }

        [TestMethod]
        public async Task GetSummaryAsync_SanitizesUnexpectedException()
        {
            var thrown = new InvalidOperationException("sensitive internal detail that must not leak");
            var handler = new Mock<IRequestHandler<GetDashboardSummaryRequest, GetDashboardSummaryResponse>>();
            handler.Setup(h => h.ExecuteAsync(It.IsAny<GetDashboardSummaryRequest>())).ThrowsAsync(thrown);

            var controller = new DashboardController(handler.Object, _getApprovalActivityHandler, _logger.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Role, Roles.DASHBOARD_READER)
                })
            };

            var response = await controller.GetSummaryAsync();

            var result = response as ObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.InternalServerError, result.StatusCode);

            var problem = result.Value as ProblemDetails;
            Assert.IsNotNull(problem);
            AssertNoExceptionLeak.Assert(problem.Detail ?? string.Empty, thrown);

            _logger.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    thrown,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once,
                "The full exception must be logged on the server side.");
        }

        [TestMethod]
        public async Task GetApprovalActivityAsync_ReturnsAggregatesFromTheRepository()
        {
            var activity = new ApprovalActivity
            {
                WindowDays = 30,
                SinceUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                QueriedAtUtc = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
                AutomatedTotal = 17,
                ManualTotal = 482,
                RejectedTotal = 83,
                Reviewers = new List<ReviewerActivityRow>
                {
                    new() { ReviewerKey = "r1", DisplayName = "Ada Lovelace", Approved = 400, Rejected = 60 },
                    new() { ReviewerKey = "__other__", DisplayName = "Other", Approved = 82, Rejected = 23, IsOther = true }
                },
                Days = new List<ApprovalActivityDay>
                {
                    new()
                    {
                        Date = new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc),
                        Automated = 2,
                        Reviewers = new List<ReviewerDayCount>
                        {
                            new() { ReviewerKey = "r1", Approved = 10, Rejected = 1 }
                        }
                    }
                }
            };

            _databaseSyncJobsRepository.Setup(x => x.GetApprovalActivityAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                                       .ReturnsAsync(activity);

            var response = await _dashboardController.GetApprovalActivityAsync(30);

            var result = response as OkObjectResult;
            Assert.IsNotNull(result);

            var activityResponse = result.Value as GetApprovalActivityResponse;
            Assert.IsNotNull(activityResponse);
            Assert.AreEqual(HttpStatusCode.OK, activityResponse.StatusCode);
            Assert.AreEqual(30, activityResponse.Activity.WindowDays);
            Assert.AreEqual(17, activityResponse.Activity.AutomatedTotal);
            Assert.AreEqual(482, activityResponse.Activity.ManualTotal);
            Assert.AreEqual(83, activityResponse.Activity.RejectedTotal);
            Assert.AreEqual(2, activityResponse.Activity.Reviewers.Count);
            Assert.IsTrue(activityResponse.Activity.Reviewers.Last().IsOther);
        }

        [TestMethod]
        public async Task GetApprovalActivityAsync_DefaultsToASevenDayWindow()
        {
            _databaseSyncJobsRepository.Setup(x => x.GetApprovalActivityAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                                       .ReturnsAsync(new ApprovalActivity { WindowDays = 7 });

            await _dashboardController.GetApprovalActivityAsync();

            _databaseSyncJobsRepository.Verify(x => x.GetApprovalActivityAsync(7, It.IsAny<CancellationToken>()), Times.Once);
        }

        [TestMethod]
        public async Task GetApprovalActivityAsync_ReturnsInternalServerErrorWhenTheRepositoryFails()
        {
            _databaseSyncJobsRepository.Setup(x => x.GetApprovalActivityAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                                       .ThrowsAsync(new Exception("An error occurred"));

            var response = await _dashboardController.GetApprovalActivityAsync(7);

            var result = response as ObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.InternalServerError, result.StatusCode);
        }

        [TestMethod]
        public async Task GetApprovalActivityAsync_SanitizesUnexpectedException()
        {
            var thrown = new InvalidOperationException("sensitive internal detail that must not leak");
            var handler = new Mock<IRequestHandler<GetApprovalActivityRequest, GetApprovalActivityResponse>>();
            handler.Setup(h => h.ExecuteAsync(It.IsAny<GetApprovalActivityRequest>())).ThrowsAsync(thrown);

            var controller = new DashboardController(_getDashboardSummaryHandler, handler.Object, _logger.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Role, Roles.DASHBOARD_READER)
                })
            };

            var response = await controller.GetApprovalActivityAsync(7);

            var result = response as ObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.InternalServerError, result.StatusCode);

            var problem = result.Value as ProblemDetails;
            Assert.IsNotNull(problem);
            AssertNoExceptionLeak.Assert(problem.Detail ?? string.Empty, thrown);
        }

        private ControllerContext CreateControllerContext(List<Claim> claims)
        {
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

            return new ControllerContext { HttpContext = httpContext };
        }
    }
}
