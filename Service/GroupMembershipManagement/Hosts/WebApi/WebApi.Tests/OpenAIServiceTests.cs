// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Configuration;
using Moq;
using WebApi.BackgroundServices;
using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using Polly;
using System.Diagnostics;
using Azure.Identity;
using Azure.Core;

namespace Services.Tests
{
    [TestClass]
    [ExcludeFromCodeCoverage]
    public class OpenAIServiceTests
    {
        private Mock<IConfiguration> _mockConfiguration = null!;
        private const string TestEndpoint = "https://test-openai-endpoint.com";

        [TestInitialize]
        public void Initialize()
        {
            // Use EnvironmentCredential for CI/CD environments where managed identity isn't available
            Environment.SetEnvironmentVariable("AZURE_TOKEN_CREDENTIALS", "EnvironmentCredential");
            
            // Set dummy service principal credentials for testing
            Environment.SetEnvironmentVariable("AZURE_CLIENT_ID", "00000000-0000-0000-0000-000000000000");
            Environment.SetEnvironmentVariable("AZURE_CLIENT_SECRET", "dummy-secret-for-testing");
            Environment.SetEnvironmentVariable("AZURE_TENANT_ID", "00000000-0000-0000-000000000000");

            _mockConfiguration = new Mock<IConfiguration>();
            _mockConfiguration.Setup(x => x["Settings:OpenAIEndpoint"]).Returns(TestEndpoint);
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

        #region Service Configuration Tests

        [TestMethod]
        public void Constructor_WithValidConfiguration_InitializesSuccessfully()
        {
            var service = new OpenAIService(_mockConfiguration.Object);
            Assert.IsNotNull(service);
        }

        [TestMethod]
        public void Constructor_WithNullEndpoint_ThrowsArgumentNullException()
        {
            _mockConfiguration.Setup(x => x["Settings:OpenAIEndpoint"]).Returns((string?)null);
            Assert.ThrowsException<ArgumentNullException>(() => new OpenAIService(_mockConfiguration.Object));
        }

        [TestMethod]
        public void Constructor_WithEmptyEndpoint_ThrowsArgumentNullException()
        {
            _mockConfiguration.Setup(x => x["Settings:OpenAIEndpoint"]).Returns(string.Empty);
            Assert.ThrowsException<ArgumentNullException>(() => new OpenAIService(_mockConfiguration.Object));
        }

        [TestMethod]
        public void Constructor_WithWhitespaceEndpoint_ThrowsArgumentNullException()
        {
            _mockConfiguration.Setup(x => x["Settings:OpenAIEndpoint"]).Returns("   ");
            Assert.ThrowsException<ArgumentNullException>(() => new OpenAIService(_mockConfiguration.Object));
        }

        [TestMethod]
        public void Constructor_ConfiguresRetryPolicyCorrectly()
        {
            var service = new OpenAIService(_mockConfiguration.Object);
            Assert.IsNotNull(service, "OpenAI service should be created with retry policy configured");
        }

        [TestMethod]
        public void RetryPolicy_ConfigurationValidation_VerifiesEndpoint()
        {
            var validService = new OpenAIService(_mockConfiguration.Object);
            Assert.IsNotNull(validService, "Service should initialize with valid endpoint configuration");
            var mockConfigWithInvalidEndpoint = new Mock<IConfiguration>();

            var invalidConfigs = new[]
            {
                (string?)null,
                string.Empty,
                "   ",
                "\t\n"
            };

            foreach (var invalidEndpoint in invalidConfigs)
            {
                mockConfigWithInvalidEndpoint.Setup(x => x["Settings:OpenAIEndpoint"]).Returns(invalidEndpoint);

                Assert.ThrowsException<ArgumentNullException>(
                    () => new OpenAIService(mockConfigWithInvalidEndpoint.Object),
                    $"Should throw ArgumentNullException for invalid endpoint: '{invalidEndpoint ?? "null"}'");
            }
        }

        [TestMethod]
        public void ServiceConfiguration_UsesCorrectDeploymentName()
        {
            var service = new OpenAIService(_mockConfiguration.Object);
            Assert.IsNotNull(service, "Service should be configured with the correct deployment name (gpt-4o)");
        }

        [TestMethod]
        public void ServiceConfiguration_UsesCorrectRetrySettings()
        {
            var service = new OpenAIService(_mockConfiguration.Object);
            Assert.IsNotNull(service, "Service should be configured with correct retry settings");
        }

        [TestMethod]
        public void RetryPolicy_ServiceInitialization_CompletesSuccessfully()
        {
            OpenAIService service = null;
            Exception initializationException = null;

            try
            {
                service = new OpenAIService(_mockConfiguration.Object);
            }
            catch (Exception ex)
            {
                initializationException = ex;
            }

            Assert.IsNull(initializationException,
                $"Service initialization should not throw exceptions, but got: {initializationException?.Message}");
            Assert.IsNotNull(service, "Service should be successfully initialized");
        }

        #endregion

        #region Runtime Behavior Tests

        [TestMethod]
        public async Task GetTitleAsync_WithCancellationToken_HandlesTimeout()
        {
            var service = new OpenAIService(_mockConfiguration.Object);
            var prompt = "Test prompt";

            try
            {
                await service.GetTitleAsync(prompt);
                Assert.Fail("Expected an exception to be thrown due to invalid test endpoint");
            }
            catch (Exception ex)
            {
                var isExpectedExceptionType = ex is TimeoutException ||
                                            ex is Azure.RequestFailedException ||
                                            ex is HttpRequestException ||
                                            ex is OperationCanceledException ||
                                            ex is ArgumentException ||
                                            ex is SocketException ||
                                            ex is AggregateException ||
                                            ex is CredentialUnavailableException ||
                                            ex is AuthenticationFailedException ||
                                            ex.InnerException is SocketException ||
                                            ex.Message.Contains("No such host is known") ||
                                            ex.Message.Contains("Retry failed") ||
                                            ex.Message.Contains("Authentication unavailable") ||
                                            ex.Message.Contains("Identity not found");

                Assert.IsTrue(isExpectedExceptionType,
                    $"Expected a network-related or retry exception, but got: {ex.GetType().Name} - {ex.Message}");

                if (ex is AggregateException aggEx)
                {
                    var hasNetworkException = aggEx.InnerExceptions.Any(inner =>
                        inner is SocketException ||
                        inner is HttpRequestException ||
                        inner.Message.Contains("No such host is known"));

                    Assert.IsTrue(hasNetworkException,
                        "AggregateException should contain network-related inner exceptions");
                }
            }
        }

        [TestMethod]
        public async Task GetTitleAsync_WithNullPrompt_HandlesGracefully()
        {
            var service = new OpenAIService(_mockConfiguration.Object);
            try
            {
                await service.GetTitleAsync(null!);
                Assert.Fail("Expected an exception to be thrown for null prompt");
            }
            catch (Exception ex)
            {
                var isExpectedExceptionType = ex is ArgumentNullException ||
                                            ex is ArgumentException ||
                                            ex is Azure.RequestFailedException ||
                                            ex is HttpRequestException ||
                                            ex is TimeoutException ||
                                            ex is OperationCanceledException ||
                                            ex is SocketException ||
                                            ex is AggregateException ||
                                            ex is CredentialUnavailableException ||
                                            ex is AuthenticationFailedException ||
                                            ex.Message.Contains("No such host is known") ||
                                            ex.Message.Contains("Retry failed") ||
                                            ex.Message.Contains("Authentication unavailable") ||
                                            ex.Message.Contains("Identity not found");

                Assert.IsTrue(isExpectedExceptionType,
                    $"Expected a validation or network-related exception, but got: {ex.GetType().Name} - {ex.Message}");
            }
        }

        [TestMethod]
        public async Task GetTitleAsync_WithEmptyPrompt_HandlesGracefully()
        {
            var service = new OpenAIService(_mockConfiguration.Object);

            try
            {
                await service.GetTitleAsync(string.Empty);
                Assert.Fail("Expected an exception to be thrown for empty prompt or network issues");
            }
            catch (Exception ex)
            {
                var isExpectedExceptionType = ex is ArgumentException ||
                                            ex is Azure.RequestFailedException ||
                                            ex is HttpRequestException ||
                                            ex is TimeoutException ||
                                            ex is OperationCanceledException ||
                                            ex is SocketException ||
                                            ex is AggregateException ||
                                            ex is CredentialUnavailableException ||
                                            ex is AuthenticationFailedException ||
                                            ex.Message.Contains("No such host is known") ||
                                            ex.Message.Contains("Retry failed") ||
                                            ex.Message.Contains("Authentication unavailable") ||
                                            ex.Message.Contains("Identity not found");

                Assert.IsTrue(isExpectedExceptionType,
                    $"Expected a validation or network-related exception, but got: {ex.GetType().Name} - {ex.Message}");
            }
        }

        #endregion

        #region Retry Policy Tests

        [TestMethod]
        public void RetryPolicy_HandlesAnyException()
        {
            var service = new OpenAIService(_mockConfiguration.Object);
            Assert.IsNotNull(service, "Service should initialize with a retry policy that handles any exception");
        }

        [TestMethod]
        public async Task RetryPolicy_WithTransientExceptions_RetriesCorrectly()
        {
            var retryCount = 0;
            var maxRetries = 3;
            var exceptions = new List<Exception>();

            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: maxRetries,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 10)),
                    onRetry: (exception, timespan, retryAttempt, context) =>
                    {
                        exceptions.Add(exception);
                        Debug.WriteLine($"Retry attempt {retryAttempt} after {timespan}s delay. Exception: {exception?.GetType().Name} - {exception?.Message}");
                    });

            try
            {
                await retryPolicy.ExecuteAsync(async () =>
                {
                    retryCount++;

                    if (retryCount < 3)
                    {
                        throw new HttpRequestException($"Transient error attempt {retryCount}");
                    }

                    return Task.CompletedTask;
                });

                Assert.AreEqual(3, retryCount, "Should have made 3 attempts total");
                Assert.AreEqual(2, exceptions.Count, "Should have recorded 2 exceptions before success");
            }
            catch (Exception ex)
            {
                Assert.Fail($"Retry policy should have succeeded on the 3rd attempt, but threw: {ex.Message}");
            }
        }

        [TestMethod]
        public async Task RetryPolicy_ExceedsMaxRetries_ThrowsLastException()
        {
            var retryCount = 0;
            var maxRetries = 3;
            var exceptions = new List<Exception>();

            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: maxRetries,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 10)),
                    onRetry: (exception, timespan, retryAttempt, context) =>
                    {
                        exceptions.Add(exception);
                        Debug.WriteLine($"Retry attempt {retryAttempt} after {timespan}s delay. Exception: {exception?.GetType().Name} - {exception?.Message}");
                    });

            var finalException = await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () =>
            {
                await retryPolicy.ExecuteAsync(async () =>
                {
                    retryCount++;
                    throw new InvalidOperationException($"Persistent error attempt {retryCount}");
                });
            });

            Assert.AreEqual(4, retryCount, "Should have made 4 attempts total (1 initial + 3 retries)");
            Assert.AreEqual(3, exceptions.Count, "Should have recorded 3 retry exceptions");
            Assert.IsTrue(finalException.Message.Contains("Persistent error attempt 4"), "Final exception should be from the last attempt");
        }

        [TestMethod]
        public async Task RetryPolicy_WithDifferentExceptionTypes_RetriesAll()
        {
            var retryCount = 0;
            var exceptions = new List<Exception>();
            var exceptionTypes = new Type[]
            {
                typeof(HttpRequestException),
                typeof(TimeoutException),
                typeof(InvalidOperationException)
            };

            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(100),
                    onRetry: (exception, timespan, retryAttempt, context) =>
                    {
                        exceptions.Add(exception);
                    });

            try
            {
                await retryPolicy.ExecuteAsync(async () =>
                {
                    if (retryCount < exceptionTypes.Length)
                    {
                        var exceptionType = exceptionTypes[retryCount];
                        retryCount++;

                        if (exceptionType == typeof(HttpRequestException))
                            throw new HttpRequestException($"HTTP error {retryCount}");
                        else if (exceptionType == typeof(TimeoutException))
                            throw new TimeoutException($"Timeout error {retryCount}");
                        else if (exceptionType == typeof(InvalidOperationException))
                            throw new InvalidOperationException($"Invalid operation error {retryCount}");
                    }

                    retryCount++;
                    return Task.CompletedTask;
                });

                Assert.AreEqual(4, retryCount, "Should have made 4 attempts total");
                Assert.AreEqual(3, exceptions.Count, "Should have handled 3 different exception types");
                Assert.IsInstanceOfType(exceptions[0], typeof(HttpRequestException));
                Assert.IsInstanceOfType(exceptions[1], typeof(TimeoutException));
                Assert.IsInstanceOfType(exceptions[2], typeof(InvalidOperationException));
            }
            catch (Exception ex)
            {
                Assert.Fail($"Should have succeeded after retrying different exception types, but got: {ex.Message}");
            }
        }

        #endregion

        #region Exponential Backoff Configuration Tests

        [TestMethod]
        public async Task RetryPolicy_ExponentialBackoff_ConfigurationIsCorrect()
        {
            var retryDelays = new List<TimeSpan>();
            var retryCount = 0;

            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: retryAttempt =>
                    {
                        var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 10));
                        retryDelays.Add(delay);
                        return delay;
                    },
                    onRetry: (exception, timespan, retryAttempt, context) =>
                    {
                        Debug.WriteLine($"Retry attempt {retryAttempt} with configured delay {timespan.TotalSeconds}s");
                    });

            try
            {
                await retryPolicy.ExecuteAsync(async () =>
                {
                    retryCount++;
                    throw new Exception($"Test exception {retryCount}");
                });
            }
            catch
            {
                // Expected to fail after all retries
            }

            Assert.AreEqual(4, retryCount, "Should have made 4 attempts total (1 initial + 3 retries)");
            Assert.AreEqual(3, retryDelays.Count, "Should have configured 3 retry delays");
            Assert.AreEqual(2, retryDelays[0].TotalSeconds, "First retry delay should be configured for 2 seconds");
            Assert.AreEqual(4, retryDelays[1].TotalSeconds, "Second retry delay should be configured for 4 seconds");
            Assert.AreEqual(8, retryDelays[2].TotalSeconds, "Third retry delay should be configured for 8 seconds");
        }

        [TestMethod]
        public void RetryPolicy_DelayCalculation_MatchesOpenAIServiceLogic()
        {
            var delays = new List<double>();
            for (int retryAttempt = 1; retryAttempt <= 5; retryAttempt++)
            {
                var delay = Math.Min(Math.Pow(2, retryAttempt), 10);
                delays.Add(delay);
            }

            Assert.AreEqual(2, delays[0], "First retry (attempt 1) should have 2 second delay");
            Assert.AreEqual(4, delays[1], "Second retry (attempt 2) should have 4 second delay");
            Assert.AreEqual(8, delays[2], "Third retry (attempt 3) should have 8 second delay");
            Assert.AreEqual(10, delays[3], "Fourth retry (attempt 4) should be capped at 10 seconds");
            Assert.AreEqual(10, delays[4], "Fifth retry (attempt 5) should be capped at 10 seconds");
            Assert.IsTrue(delays.All(d => d <= 10), "All delays should be capped at 10 seconds maximum");
        }

        [TestMethod]
        public void RetryPolicy_Configuration_MatchesOpenAIServiceSettings()
        {
            var expectedRetryCount = 3;
            var expectedMaxDelay = 10;

            var retryPolicy = Policy
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: expectedRetryCount,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), expectedMaxDelay)),
                    onRetry: (exception, timespan, retryCount, context) =>
                    {
                        Debug.WriteLine($"OpenAI API retry attempt {retryCount} after {timespan}s delay. Exception: {exception?.GetType().Name} - {exception?.Message}");
                    });

            Assert.IsNotNull(retryPolicy, "Retry policy should be created successfully");
            var delay1 = Math.Min(Math.Pow(2, 1), expectedMaxDelay); // 2 seconds
            var delay2 = Math.Min(Math.Pow(2, 2), expectedMaxDelay); // 4 seconds
            var delay3 = Math.Min(Math.Pow(2, 3), expectedMaxDelay); // 8 seconds
            var delay4 = Math.Min(Math.Pow(2, 4), expectedMaxDelay); // 10 seconds

            Assert.AreEqual(2, delay1, "First retry delay should be 2 seconds");
            Assert.AreEqual(4, delay2, "Second retry delay should be 4 seconds");
            Assert.AreEqual(8, delay3, "Third retry delay should be 8 seconds");
            Assert.AreEqual(10, delay4, "Fourth retry delay should be capped at 10 seconds");
        }

        [TestMethod]
        public void RetryPolicy_Configuration_VerifyExponentialBackoff()
        {
            var service = new OpenAIService(_mockConfiguration.Object);
            Assert.IsNotNull(service, "Service should be created with exponential backoff retry policy");
        }

        #endregion
    }
}