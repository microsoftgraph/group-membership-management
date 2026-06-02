// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WebApi.Controllers.v1.OpenAI;
using WebApi.BackgroundServices;
using System.Text.Json;
using Models;
using Microsoft.AspNetCore.Mvc;
using Azure;
using Services.WebApi.Contracts;
using WebApi.Tests.ExceptionHandling;

namespace WebApi.Tests
{
    [TestClass]
    public class OpenAIControllerTests
    {
        private Mock<IOpenAIService> _mockOpenAIService = null!;
        private OpenAIController _controller = null!;

        [TestInitialize]
        public void Initialize()
        {
            Environment.SetEnvironmentVariable("AZURE_TOKEN_CREDENTIALS", "EnvironmentCredential");

            // Set dummy service principal credentials for testing
            Environment.SetEnvironmentVariable("AZURE_CLIENT_ID", "00000000-0000-0000-0000-000000000000");
            Environment.SetEnvironmentVariable("AZURE_CLIENT_SECRET", "dummy-secret-for-testing");
            Environment.SetEnvironmentVariable("AZURE_TENANT_ID", "00000000-0000-0000-000000000000");

            _mockOpenAIService = new Mock<IOpenAIService>();
            _controller = new OpenAIController(_mockOpenAIService.Object, NullLogger<OpenAIController>.Instance);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Clean up environment variables after each test
            Environment.SetEnvironmentVariable("AZURE_TOKEN_CREDENTIALS", null);
            Environment.SetEnvironmentVariable("AZURE_TOKEN_CREDENTIALS", null);
            Environment.SetEnvironmentVariable("AZURE_CLIENT_ID", null);
            Environment.SetEnvironmentVariable("AZURE_CLIENT_SECRET", null);
            Environment.SetEnvironmentVariable("AZURE_TENANT_ID", null);
        }

        [TestMethod]
        public void Constructor_WithNullService_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new OpenAIController(null!, NullLogger<OpenAIController>.Instance));
        }

        [TestMethod]
        public void Constructor_WithNullLogger_ThrowsArgumentNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() => new OpenAIController(_mockOpenAIService.Object, null!));
        }

        [TestMethod]
        public void Constructor_ValidatesOpenAIServiceDependency()
        {
            // Test that the controller properly validates the OpenAI service dependency
            var mockConfiguration = new Mock<IConfiguration>();
            mockConfiguration.Setup(x => x["Settings:OpenAIEndpoint"]).Returns("https://test-endpoint.com");

            var openAIService = new OpenAIService(mockConfiguration.Object);
            var controller = new OpenAIController(openAIService, NullLogger<OpenAIController>.Instance);
            Assert.IsNotNull(controller, "Controller should be created successfully with valid OpenAI service");
        }

        [TestMethod]
        public void OpenAIService_ThrowsExceptionWithInvalidEndpoint()
        {
            // Test that OpenAI service throws exception when endpoint is not configured
            var mockConfiguration = new Mock<IConfiguration>();
            mockConfiguration.Setup(x => x["Settings:OpenAIEndpoint"]).Returns((string?)null);

            Assert.ThrowsException<ArgumentNullException>(() => new OpenAIService(mockConfiguration.Object),
                "OpenAI service should throw ArgumentNullException when endpoint is not configured");
        }

        [TestMethod]
        public async Task GenerateTitle_WithNullFilter_ReturnsBadRequest()
        {
            // Act
            var result = await _controller.GenerateTitle(null);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            var badRequestResult = result as BadRequestObjectResult;
            Assert.IsNotNull(badRequestResult?.Value);
        }

        [TestMethod]
        public async Task GenerateTitle_WithEmptyFilter_ReturnsBadRequest()
        {
            // Act
            var result = await _controller.GenerateTitle("");

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            var badRequestResult = result as BadRequestObjectResult;
            Assert.IsNotNull(badRequestResult?.Value);
        }

        [TestMethod]
        public async Task GenerateTitle_WithWhitespaceFilter_ReturnsBadRequest()
        {
            // Act
            var result = await _controller.GenerateTitle("   ");

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
            var badRequestResult = result as BadRequestObjectResult;
            Assert.IsNotNull(badRequestResult?.Value);
        }

        [TestMethod]
        public async Task GenerateTitle_WithValidFilter_ReturnsOkWithTitle()
        {
            // Arrange
            var filter = "EmployeeType_Code = 'FTE'";
            var expectedTitle = "FTE Employees";
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(expectedTitle);

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = result as OkObjectResult;
            Assert.AreEqual(expectedTitle, okResult?.Value);
        }

        [TestMethod]
        public async Task GenerateTitle_WithInvalidOperationException_ReturnsInternalServerError()
        {
            // Arrange
            var filter = "EmployeeType_Code = 'FTE'";
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("Service not configured"));

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitle_WithRequestFailedException500_ReturnsInternalServerError()
        {
            // Arrange
            var filter = "EmployeeType_Code = 'FTE'";
            var requestFailedException = new RequestFailedException(500, "Internal server error", "InternalError", null);
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(requestFailedException);

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitle_WithGenericException_ReturnsInternalServerError()
        {
            // Arrange
            var filter = "EmployeeType_Code = 'FTE'";
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(new Exception("Unexpected error"));

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithNullParts_ReturnsBadRequest()
        {
            // Act
            var result = await _controller.GenerateTitles(null);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(BadRequestObjectResult));
        }

        [TestMethod]
        public async Task GenerateTitles_WithEmptyParts_ReturnsBadRequest()
        {
            // Act
            var result = await _controller.GenerateTitles(new List<Part>());

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(BadRequestObjectResult));
        }

        [TestMethod]
        public async Task GenerateTitles_WithInvalidParts_ReturnsBadRequest()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.Empty, ""), // Invalid part
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(BadRequestObjectResult));
        }

        [TestMethod]
        public async Task GenerateTitles_WithPartsHavingInvalidFilter_ReturnsBadRequest()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "   "), // Whitespace filter
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(BadRequestObjectResult));
        }

        [TestMethod]
        public async Task GenerateTitles_WithValidParts_ReturnsOkWithTitles()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'"),
                new Part(Guid.NewGuid(), "LocationArea_Code = 'US'")
            };

            var expectedResponse = new List<Part>
            {
                new Part(parts[0].PartId, parts[0].Filter, "FTE Employees"),
                new Part(parts[1].PartId, parts[1].Filter, "Located in US")
            };

            var responseJson = JsonSerializer.Serialize(expectedResponse);
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(responseJson);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(OkObjectResult));
            var okResult = result.Result as OkObjectResult;
            var returnedParts = okResult?.Value as List<Part>;
            Assert.IsNotNull(returnedParts);
            Assert.AreEqual(2, returnedParts.Count);
            Assert.IsTrue(returnedParts.All(p => p.HasTitle()));
        }

        [TestMethod]
        public async Task GenerateTitles_WithEmptyOpenAIResponse_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(string.Empty);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithInvalidJsonResponse_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync("invalid json response");

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithMismatchedPartCount_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'"),
                new Part(Guid.NewGuid(), "LocationArea_Code = 'US'")
            };

            // Return only one title for two parts
            var responseJson = JsonSerializer.Serialize(new List<Part>
            {
                new Part(parts[0].PartId, parts[0].Filter, "FTE Employees")
            });

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(responseJson);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithMissingPartIds_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'"),
                new Part(Guid.NewGuid(), "LocationArea_Code = 'US'")
            };

            // Return different PartIds than input
            var responseJson = JsonSerializer.Serialize(new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'", "FTE Employees"),
                new Part(Guid.NewGuid(), "LocationArea_Code = 'US'", "Located in US")
            });

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(responseJson);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithEmptyTitles_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            var responseJson = JsonSerializer.Serialize(new List<Part>
            {
                new Part(parts[0].PartId, parts[0].Filter, "") // Empty title
            });

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(responseJson);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithArgumentException_ReturnsBadRequest()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(new ArgumentException("Invalid argument"));

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(BadRequestObjectResult));
        }

        [TestMethod]
        public async Task GenerateTitles_WithInvalidOperationException_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("Service not configured"));

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithRequestFailedException429_ReturnsServiceUnavailable()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            var requestFailedException = new RequestFailedException(429, "Rate limited", "TooManyRequests", null);
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(requestFailedException);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(503, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithRequestFailedException500_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            var requestFailedException = new RequestFailedException(500, "Internal server error", "InternalError", null);
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(requestFailedException);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithGenericException_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(new Exception("Unexpected error"));

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitle_WithTimeoutException_ReturnsInternalServerError()
        {
            // Arrange
            var filter = "EmployeeType_Code = 'FTE'";
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(new TimeoutException("OpenAI API call timed out after 30 seconds"));

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
            Assert.IsNotNull(objectResult?.Value);
        }

        [TestMethod]
        public async Task GenerateTitles_WithTimeoutException_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(new TimeoutException("OpenAI API call timed out after 30 seconds"));

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitle_WithTaskCanceledException_ReturnsInternalServerError()
        {
            // Arrange
            var filter = "EmployeeType_Code = 'FTE'";
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(new TaskCanceledException("Operation was canceled"));

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithTaskCanceledException_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(new TaskCanceledException("Operation was canceled"));

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitle_WithHttpRequestException_ReturnsInternalServerError()
        {
            // Arrange
            var filter = "EmployeeType_Code = 'FTE'";
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(new HttpRequestException("HTTP request failed"));

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithHttpRequestException_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(new HttpRequestException("HTTP request failed"));

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitle_WithRequestFailedException502_ReturnsInternalServerError()
        {
            // Arrange
            var filter = "EmployeeType_Code = 'FTE'";
            var requestFailedException = new RequestFailedException(502, "Bad gateway", "BadGateway", null);
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(requestFailedException);

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithRequestFailedException502_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            var requestFailedException = new RequestFailedException(502, "Bad gateway", "BadGateway", null);
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(requestFailedException);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitle_WithRequestFailedException503_ReturnsInternalServerError()
        {
            // Arrange
            var filter = "EmployeeType_Code = 'FTE'";
            var requestFailedException = new RequestFailedException(503, "Service unavailable", "ServiceUnavailable", null);
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(requestFailedException);

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithRequestFailedException503_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            var requestFailedException = new RequestFailedException(503, "Service unavailable", "ServiceUnavailable", null);
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(requestFailedException);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitle_WithRequestFailedException504_ReturnsInternalServerError()
        {
            // Arrange
            var filter = "EmployeeType_Code = 'FTE'";
            var requestFailedException = new RequestFailedException(504, "Gateway timeout", "GatewayTimeout", null);
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(requestFailedException);

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(ObjectResult));
            var objectResult = result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithRequestFailedException504_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            var requestFailedException = new RequestFailedException(504, "Gateway timeout", "GatewayTimeout", null);
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(requestFailedException);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitle_WithNullResult_ReturnsOkWithNull()
        {
            // Arrange
            var filter = "EmployeeType_Code = 'FTE'";
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync((string?)null);

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = result as OkObjectResult;
            Assert.IsNull(okResult?.Value);
        }

        [TestMethod]
        public async Task GenerateTitles_WithNullFromOpenAI_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync((string?)null);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithWhitespaceOnlyFromOpenAI_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync("   ");

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithDeserializedNullList_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            var responseJson = "null";
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(responseJson);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithEmptyDeserializedList_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            var responseJson = "[]";
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(responseJson);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithMalformedJson_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync("{invalid json}");

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitles_WithPartiallyValidJson_ReturnsInternalServerError()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync("[{\"partId\":\"123\",\"title\":\"test\"},invalid]");

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(ObjectResult));
            var objectResult = result.Result as ObjectResult;
            Assert.AreEqual(500, objectResult?.StatusCode);
        }

        [TestMethod]
        public async Task GenerateTitle_WithEmptyStringResult_ReturnsOkWithEmptyString()
        {
            // Arrange
            var filter = "EmployeeType_Code = 'FTE'";
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(""); // Semicolon was missing here

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = result as OkObjectResult;
            Assert.AreEqual("", okResult?.Value);
        }

        [TestMethod]
        public async Task GenerateTitle_WithWhitespaceResult_ReturnsOkWithWhitespace()
        {
            // Arrange
            var filter = "EmployeeType_Code = 'FTE'";
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync("   ");

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = result as OkObjectResult;
            Assert.AreEqual("   ", okResult?.Value);
        }

        [TestMethod]
        public async Task GenerateTitle_WithVeryLongFilter_ReturnsOkWithTitle()
        {
            // Arrange
            var filter = new string('A', 10000); // Very long filter
            var expectedTitle = "Long Filter Title";
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(expectedTitle);

            // Act
            var result = await _controller.GenerateTitle(filter);

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = result as OkObjectResult;
            Assert.AreEqual(expectedTitle, okResult?.Value);
        }

        [TestMethod]
        public async Task GenerateTitles_WithManyParts_ReturnsOkWithTitles()
        {
            // Arrange
            var parts = Enumerable.Range(0, 100)
                .Select(i => new Part(Guid.NewGuid(), $"EmployeeType_Code = 'Type{i}'"))
                .ToList();

            var expectedResponse = parts.Select(p => new Part(p.PartId, p.Filter, "EmployeeType Title")).ToList();
            var responseJson = JsonSerializer.Serialize(expectedResponse);
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(responseJson);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(OkObjectResult));
            var okResult = result.Result as OkObjectResult;
            var returnedParts = okResult?.Value as List<Part>;
            Assert.IsNotNull(returnedParts);
            Assert.AreEqual(100, returnedParts.Count);
            Assert.IsTrue(returnedParts.All(p => p.HasTitle()));
        }

        [TestMethod]
        public async Task GenerateTitles_WithDuplicatePartIds_ReturnsOkIfAllPresent()
        {
            // Arrange - This tests when duplicate PartIds are in input but all are returned
            var partId = Guid.NewGuid();
            var parts = new List<Part>
            {
                new Part(partId, "EmployeeType_Code = 'FTE'"),
                new Part(partId, "LocationArea_Code = 'US'") // Duplicate PartId
            };

            var expectedResponse = new List<Part>
            {
                new Part(partId, "EmployeeType_Code = 'FTE'", "FTE Employees"),
                new Part(partId, "LocationArea_Code = 'US'", "Located in US")
            };

            var responseJson = JsonSerializer.Serialize(expectedResponse);
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(responseJson);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(OkObjectResult));
            var okResult = result.Result as OkObjectResult;
            var returnedParts = okResult?.Value as List<Part>;
            Assert.IsNotNull(returnedParts);
            Assert.AreEqual(2, returnedParts.Count);
        }

        [TestMethod]
        public async Task GenerateTitles_WithSpecialCharactersInTitle_ReturnsOkWithTitles()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            var expectedResponse = new List<Part>
            {
                new Part(parts[0].PartId, parts[0].Filter, "FTE Employees")
            };

            var responseJson = JsonSerializer.Serialize(expectedResponse);
            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(responseJson);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            Assert.IsInstanceOfType(result.Result, typeof(OkObjectResult));
            var okResult = result.Result as OkObjectResult;
            var returnedParts = okResult?.Value as List<Part>;
            Assert.IsNotNull(returnedParts);
            Assert.AreEqual(1, returnedParts.Count);
            Assert.AreEqual("FTE Employees", returnedParts[0].Title);
        }

        [TestMethod]
        public async Task GenerateTitles_CallsOpenAIServiceWithCorrectPrompt()
        {
            // Arrange
            var parts = new List<Part>
            {
                new Part(Guid.NewGuid(), "EmployeeType_Code = 'FTE'")
            };

            var expectedResponse = new List<Part>
            {
                new Part(parts[0].PartId, parts[0].Filter, "FTE Employees")
            };

            var responseJson = JsonSerializer.Serialize(expectedResponse);
            string capturedPrompt = null!;

            _mockOpenAIService.Setup(x => x.GetTitleAsync(It.IsAny<string>()))
                .Callback<string>(prompt => capturedPrompt = prompt)
                .ReturnsAsync(responseJson);

            // Act
            var result = await _controller.GenerateTitles(parts);

            // Assert
            _mockOpenAIService.Verify(x => x.GetTitleAsync(It.IsAny<string>()), Times.Once);
            Assert.IsNotNull(capturedPrompt);
            Assert.IsTrue(capturedPrompt.Contains(parts[0].PartId.ToString()));

            // The filter should be contained within the JSON structure
            var inputJson = JsonSerializer.Serialize(parts);
            Assert.IsTrue(capturedPrompt.Contains(inputJson), "Prompt should contain the serialized parts JSON");
            Assert.IsTrue(capturedPrompt.Contains("Generate short, clear titles"));
        }

        // ── Error-body sanitization tests ────────────────────────────────────────────
        // Each anonymous-body catch site in GenerateTitles must:
        //   1. Drop the `details = ex.Message` property entirely (no exception text in body)
        //   2. Preserve the generic `error` string unchanged
        //   3. Keep its existing HTTP status code
        //   4. Still log the exception via the existing partial-class LoggerMessage method,
        //      verified through the underlying `ILogger.Log<TState>(...)` call.

        private const string OpenAiTitlesJsonParseErrorMessage = "Failed to parse OpenAI response as valid JSON.";
        private const string OpenAiTitlesArgumentErrorMessage = "Invalid request parameters.";
        private const string OpenAiTitlesInvalidOperationErrorMessage = "OpenAI service is not properly configured.";
        private const string OpenAiTitlesRequestFailedErrorMessage = "OpenAI service request failed.";

        private static OpenAIController BuildControllerWithLogger(
            Mock<IOpenAIService> serviceMock,
            Mock<ILogger<OpenAIController>> loggerMock)
        {
            loggerMock.Setup(l => l.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
            return new OpenAIController(serviceMock.Object, loggerMock.Object);
        }

        private static void VerifyLoggedOnce(
            Mock<ILogger<OpenAIController>> loggerMock,
            LogLevel expectedLevel,
            int expectedEventId,
            Exception expectedException)
        {
            loggerMock.Verify(
                l => l.Log(
                    expectedLevel,
                    It.Is<EventId>(e => e.Id == expectedEventId),
                    It.IsAny<It.IsAnyType>(),
                    expectedException,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once,
                $"Expected exactly one Log call with EventId {expectedEventId} carrying the thrown exception.");
        }

        [TestMethod]
        public async Task GenerateTitles_JsonException_SanitizesResponseBody()
        {
            // Mock service returns malformed JSON so JsonSerializer.Deserialize<List<Part>> throws.
            var parts = new List<Part> { new Part(Guid.NewGuid(), "column_name = 'value'") };
            var loggerMock = new Mock<ILogger<OpenAIController>>();
            var serviceMock = new Mock<IOpenAIService>();
            const string malformedJson = "{this-is-not-valid-json:::";
            serviceMock.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ReturnsAsync(malformedJson);
            var controller = BuildControllerWithLogger(serviceMock, loggerMock);

            var result = await controller.GenerateTitles(parts);

            var objectResult = result.Result as ObjectResult;
            Assert.IsNotNull(objectResult, "Expected ObjectResult for JsonException site.");
            Assert.AreEqual(500, objectResult!.StatusCode);

            var bodyJson = JsonSerializer.Serialize(objectResult.Value);
            StringAssert.Contains(bodyJson, OpenAiTitlesJsonParseErrorMessage,
                "Generic `error` constant must be preserved unchanged.");
            Assert.IsFalse(bodyJson.Contains("\"details\"", StringComparison.OrdinalIgnoreCase),
                $"Response body must not contain a `details` property. Body was: {bodyJson}");
            Assert.IsFalse(bodyJson.Contains(malformedJson),
                $"Response body must not echo the upstream payload that caused the JsonException. Body was: {bodyJson}");

            loggerMock.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.Is<EventId>(e => e.Id == 91111),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<JsonException>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once,
                "Expected exactly one Error log at EventId 91111 carrying the JsonException.");
        }

        [TestMethod]
        public async Task GenerateTitles_ArgumentException_SanitizesResponseBody()
        {
            var parts = new List<Part> { new Part(Guid.NewGuid(), "column_name = 'value'") };
            var loggerMock = new Mock<ILogger<OpenAIController>>();
            var serviceMock = new Mock<IOpenAIService>();
            var thrown = new ArgumentException("test exception message");
            serviceMock.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(thrown);
            var controller = BuildControllerWithLogger(serviceMock, loggerMock);

            var result = await controller.GenerateTitles(parts);

            var badRequest = result.Result as BadRequestObjectResult;
            Assert.IsNotNull(badRequest, "Expected BadRequestObjectResult for ArgumentException site.");
            Assert.AreEqual(400, badRequest!.StatusCode);

            var bodyJson = JsonSerializer.Serialize(badRequest.Value);
            StringAssert.Contains(bodyJson, OpenAiTitlesArgumentErrorMessage,
                "Generic `error` constant must be preserved unchanged.");
            Assert.IsFalse(bodyJson.Contains("\"details\"", StringComparison.OrdinalIgnoreCase),
                $"Response body must not contain a `details` property. Body was: {bodyJson}");
            AssertNoExceptionLeak.Assert(bodyJson, thrown);

            VerifyLoggedOnce(loggerMock, LogLevel.Warning, 91112, thrown);
        }

        [TestMethod]
        public async Task GenerateTitles_InvalidOperationException_SanitizesResponseBody()
        {
            var parts = new List<Part> { new Part(Guid.NewGuid(), "column_name = 'value'") };
            var loggerMock = new Mock<ILogger<OpenAIController>>();
            var serviceMock = new Mock<IOpenAIService>();
            var thrown = new InvalidOperationException("test exception message");
            serviceMock.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(thrown);
            var controller = BuildControllerWithLogger(serviceMock, loggerMock);

            var result = await controller.GenerateTitles(parts);

            var objectResult = result.Result as ObjectResult;
            Assert.IsNotNull(objectResult, "Expected ObjectResult for InvalidOperationException site.");
            Assert.AreEqual(500, objectResult!.StatusCode);

            var bodyJson = JsonSerializer.Serialize(objectResult.Value);
            StringAssert.Contains(bodyJson, OpenAiTitlesInvalidOperationErrorMessage,
                "Generic `error` constant must be preserved unchanged.");
            Assert.IsFalse(bodyJson.Contains("\"details\"", StringComparison.OrdinalIgnoreCase),
                $"Response body must not contain a `details` property. Body was: {bodyJson}");
            AssertNoExceptionLeak.Assert(bodyJson, thrown);

            VerifyLoggedOnce(loggerMock, LogLevel.Error, 91113, thrown);
        }

        [TestMethod]
        public async Task GenerateTitles_RequestFailedExceptionNon429_SanitizesResponseBody()
        {
            var parts = new List<Part> { new Part(Guid.NewGuid(), "column_name = 'value'") };
            var loggerMock = new Mock<ILogger<OpenAIController>>();
            var serviceMock = new Mock<IOpenAIService>();
            var thrown = new RequestFailedException(500, "test exception message", "InternalError", null);
            serviceMock.Setup(x => x.GetTitleAsync(It.IsAny<string>())).ThrowsAsync(thrown);
            var controller = BuildControllerWithLogger(serviceMock, loggerMock);

            var result = await controller.GenerateTitles(parts);

            var objectResult = result.Result as ObjectResult;
            Assert.IsNotNull(objectResult, "Expected ObjectResult for non-429 RequestFailedException site.");
            Assert.AreEqual(500, objectResult!.StatusCode);

            var bodyJson = JsonSerializer.Serialize(objectResult.Value);
            StringAssert.Contains(bodyJson, OpenAiTitlesRequestFailedErrorMessage,
                "Generic `error` constant must be preserved unchanged.");
            Assert.IsFalse(bodyJson.Contains("\"details\"", StringComparison.OrdinalIgnoreCase),
                $"Response body must not contain a `details` property. Body was: {bodyJson}");
            AssertNoExceptionLeak.Assert(bodyJson, thrown);

            VerifyLoggedOnce(loggerMock, LogLevel.Error, 91115, thrown);
        }
    }
}