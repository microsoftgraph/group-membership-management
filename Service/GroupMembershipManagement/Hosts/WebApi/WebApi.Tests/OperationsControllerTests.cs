// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Models;
using Moq;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Net;
using System.Security.Claims;
using WebApi.Controllers.v1.Operations;
using WebApi.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WebApi.Tests.ExceptionHandling;

namespace WebApi.Tests
{
    [TestClass]
    public class OperationsControllerTests
    {
        private HttpContext _context = null!;
        private OperationsController _operationsController = null!;
        private PostOperationHandler _postResetRequestHandler = null!;
        private GetServiceStatusHandler _getServiceStatusRequestHandler = null!;
        private Mock<IServiceStatusRepository> _serviceStatusRepository = null!;
        private Mock<IOperationsTaskQueue> _backgroundTaskService = null!;
        private Mock<ILogger<OperationsController>> _logger = null!;

        [TestInitialize]
        public void Initialize()
        {
            _context = new DefaultHttpContext();

            _serviceStatusRepository = new Mock<IServiceStatusRepository>();
            _backgroundTaskService = new Mock<IOperationsTaskQueue>();
            _logger = new Mock<ILogger<OperationsController>>();

            _postResetRequestHandler = new PostOperationHandler(NullLogger<PostOperationHandler>.Instance,
                                                                _serviceStatusRepository.Object,
                                                                _backgroundTaskService.Object);

            _getServiceStatusRequestHandler = new GetServiceStatusHandler(NullLogger<GetServiceStatusHandler>.Instance,
                                                                          _serviceStatusRepository.Object);

            _operationsController = new OperationsController(_postResetRequestHandler,
                                                             _getServiceStatusRequestHandler,
                                                             _logger.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Role, Roles.RESET_ADMINISTRATOR),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };
        }

        [TestMethod]
        public async Task GetServiceStatusAsync()
        {
            _serviceStatusRepository.Setup(x => x.GetCurrentServiceStatusAsync())
                                    .ReturnsAsync(ServiceStatuses.Running);

            var response = await _operationsController.GetCurrentStatusAsync();
            var result = response as OkObjectResult;
            var serviceStatusResponse = result.Value as GetServiceStatusResponse;
            Assert.IsNotNull(serviceStatusResponse);
            Assert.AreEqual(HttpStatusCode.OK, serviceStatusResponse.StatusCode);
            Assert.AreEqual(ServiceStatuses.Running, serviceStatusResponse.Status);
        }

        [TestMethod]
        public async Task GetServiceStatusAsync_InternalServerError()
        {
            _serviceStatusRepository.Setup(x => x.GetCurrentServiceStatusAsync())
                                    .ThrowsAsync(new Exception("An error occurred"));

            var response = await _operationsController.GetCurrentStatusAsync();
            var result = response as ObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.InternalServerError, result.StatusCode);
        }

        [TestMethod]
        public async Task StopWhenRunningAsync()
        {
            var statusChecks = 0;
            _serviceStatusRepository.Setup(x => x.GetCurrentServiceStatusAsync())
                                    .ReturnsAsync(() =>
                                    {
                                        if (statusChecks == 0)
                                        {
                                            statusChecks++;
                                            return ServiceStatuses.Running;
                                        }

                                        return ServiceStatuses.Stopping;
                                    });


            var response = await _operationsController.ProcessOperationAsync(Operations.Stop);
            var result = response as OkObjectResult;
            var postOperationResponse = result.Value as PostOperationResponse;
            Assert.IsNotNull(postOperationResponse);
            Assert.AreEqual(HttpStatusCode.OK, postOperationResponse.StatusCode);
            Assert.AreEqual(ServiceStatuses.Stopping, postOperationResponse.Status);
        }

        [TestMethod]
        public async Task StopWhenStoppedAsync()
        {
            _serviceStatusRepository.Setup(x => x.GetCurrentServiceStatusAsync())
                                    .ReturnsAsync(ServiceStatuses.Stopped);

            var response = await _operationsController.ProcessOperationAsync(Operations.Stop);
            var result = response as OkObjectResult;
            var postOperationResponse = result.Value as PostOperationResponse;
            Assert.IsNotNull(postOperationResponse);
            Assert.AreEqual(HttpStatusCode.OK, postOperationResponse.StatusCode);
            Assert.AreEqual(ServiceStatuses.Stopped, postOperationResponse.Status);
        }

        [TestMethod]
        public async Task ResetWhenRunningAsync()
        {
            var statusChecks = 0;
            _serviceStatusRepository.Setup(x => x.GetCurrentServiceStatusAsync())
                                    .ReturnsAsync(() =>
                                    {
                                        if (statusChecks == 0)
                                        {
                                            statusChecks++;
                                            return ServiceStatuses.Running;
                                        }

                                        return ServiceStatuses.Resetting;
                                    });


            var response = await _operationsController.ProcessOperationAsync(Operations.Reset);
            var result = response as OkObjectResult;
            var postOperationResponse = result.Value as PostOperationResponse;
            Assert.IsNotNull(postOperationResponse);
            Assert.AreEqual(HttpStatusCode.OK, postOperationResponse.StatusCode);
            Assert.AreEqual(ServiceStatuses.Resetting, postOperationResponse.Status);
        }

        [TestMethod]
        public async Task ResetWhenResettingAsync()
        {
            _serviceStatusRepository.Setup(x => x.GetCurrentServiceStatusAsync())
                                    .ReturnsAsync(ServiceStatuses.Resetting);

            var response = await _operationsController.ProcessOperationAsync(Operations.Reset);
            var result = response as OkObjectResult;
            var postOperationResponse = result.Value as PostOperationResponse;
            Assert.IsNotNull(postOperationResponse);
            Assert.AreEqual(HttpStatusCode.OK, postOperationResponse.StatusCode);
            Assert.AreEqual(ServiceStatuses.Resetting, postOperationResponse.Status);
        }

        [TestMethod]
        public async Task StartWhenNotRunningAsync()
        {
            var statusChecks = 0;
            _serviceStatusRepository.Setup(x => x.GetCurrentServiceStatusAsync())
                                    .ReturnsAsync(() =>
                                    {
                                        if (statusChecks == 0)
                                        {
                                            statusChecks++;
                                            return ServiceStatuses.Stopped;
                                        }

                                        return ServiceStatuses.Running;
                                    });


            var response = await _operationsController.ProcessOperationAsync(Operations.Start);
            var result = response as OkObjectResult;
            var postOperationResponse = result.Value as PostOperationResponse;
            Assert.IsNotNull(postOperationResponse);
            Assert.AreEqual(HttpStatusCode.OK, postOperationResponse.StatusCode);
            Assert.AreEqual(ServiceStatuses.Running, postOperationResponse.Status);
        }

        [TestMethod]
        public async Task StartWhenRunningAsync()
        {
            _serviceStatusRepository.Setup(x => x.GetCurrentServiceStatusAsync())
                                    .ReturnsAsync(ServiceStatuses.Running);

            var response = await _operationsController.ProcessOperationAsync(Operations.Start);
            var result = response as OkObjectResult;
            var postOperationResponse = result.Value as PostOperationResponse;
            Assert.IsNotNull(postOperationResponse);
            Assert.AreEqual(HttpStatusCode.OK, postOperationResponse.StatusCode);
            Assert.AreEqual(ServiceStatuses.Running, postOperationResponse.Status);
        }

        [TestMethod]
        public async Task ProcessOperationAsync_SanitizesUnexpectedException()
        {
            var thrown = new InvalidOperationException("sensitive internal detail that must not leak (POST path)");
            var postHandler = new Mock<IRequestHandler<PostOperationRequest, PostOperationResponse>>();
            postHandler.Setup(h => h.ExecuteAsync(It.IsAny<PostOperationRequest>())).ThrowsAsync(thrown);

            var controller = new OperationsController(postHandler.Object,
                                                      _getServiceStatusRequestHandler,
                                                      _logger.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Role, Roles.RESET_ADMINISTRATOR),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };

            var response = await controller.ProcessOperationAsync(Operations.Stop);

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
        public async Task GetCurrentStatusAsync_SanitizesUnexpectedException()
        {
            var thrown = new InvalidOperationException("sensitive internal detail that must not leak (GET path)");
            var getHandler = new Mock<IRequestHandler<GetServiceStatusRequest, GetServiceStatusResponse>>();
            getHandler.Setup(h => h.ExecuteAsync(It.IsAny<GetServiceStatusRequest>())).ThrowsAsync(thrown);

            var controller = new OperationsController(_postResetRequestHandler,
                                                      getHandler.Object,
                                                      _logger.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Role, Roles.RESET_ADMINISTRATOR),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };

            var response = await controller.GetCurrentStatusAsync();

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


        private ControllerContext CreateControllerContext(List<Claim> claims)
        {
            return new ControllerContext { HttpContext = CreateHttpContext(claims) };
        }

        private HttpContext CreateHttpContext(List<Claim> claims)
        {
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var principal = new ClaimsPrincipal(identity);
            var httpContext = new DefaultHttpContext();
            httpContext.User = principal;

            return httpContext;
        }
    }
}
