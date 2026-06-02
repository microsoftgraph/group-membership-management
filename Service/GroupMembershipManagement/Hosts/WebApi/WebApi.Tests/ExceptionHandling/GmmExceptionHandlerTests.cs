// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Moq;
using WebApi.ExceptionHandling;

namespace WebApi.Tests.ExceptionHandling
{
    [TestClass]
    public class GmmExceptionHandlerTests
    {
        private Mock<ILogger<GmmExceptionHandler>> _logger = null!;
        private Mock<IProblemDetailsService> _problemDetailsService = null!;
        private GmmExceptionHandler _handler = null!;
        private HttpContext _context = null!;

        [TestInitialize]
        public void Initialize()
        {
            _logger = new Mock<ILogger<GmmExceptionHandler>>();
            _problemDetailsService = new Mock<IProblemDetailsService>();
            _problemDetailsService
                .Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
                .ReturnsAsync(true);

            _handler = new GmmExceptionHandler(_logger.Object, _problemDetailsService.Object);

            _context = new DefaultHttpContext();
            _context.Request.Method = "GET";
            _context.Request.Path = "/api/v1/jobs";
            _context.TraceIdentifier = "test-trace-identifier-abc";
        }

        [TestMethod]
        public async Task TryHandleAsync_ReturnsTrue_ForAnyException()
        {
            // Arrange
            var ex = new InvalidOperationException("anything");

            // Act
            var handled = await _handler.TryHandleAsync(_context, ex, CancellationToken.None);

            // Assert
            Assert.IsTrue(handled, "TryHandleAsync must return true so the framework suppresses downstream error handling.");
        }

        [TestMethod]
        public async Task TryHandleAsync_SetsStatusCode500()
        {
            // Arrange
            var ex = new Exception("boom");

            // Act
            await _handler.TryHandleAsync(_context, ex, CancellationToken.None);

            // Assert
            Assert.AreEqual(StatusCodes.Status500InternalServerError, _context.Response.StatusCode);
        }

        [TestMethod]
        public async Task TryHandleAsync_WritesProblemDetailsWithGenericDetail()
        {
            // Arrange
            var ex = new Exception("sensitive internal detail that must not leak");
            ProblemDetailsContext? captured = null;
            _problemDetailsService
                .Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
                .Callback<ProblemDetailsContext>(c => captured = c)
                .ReturnsAsync(true);

            // Act
            await _handler.TryHandleAsync(_context, ex, CancellationToken.None);

            // Assert
            Assert.IsNotNull(captured, "ProblemDetailsService.TryWriteAsync must be invoked exactly once.");
            Assert.AreEqual(StatusCodes.Status500InternalServerError, captured!.ProblemDetails.Status);
            Assert.AreEqual("An unexpected error occurred.", captured.ProblemDetails.Detail);
            Assert.IsFalse(captured.ProblemDetails.Detail!.Contains("sensitive", StringComparison.Ordinal),
                "Sanitized detail must not contain any substring of the original exception message.");
            Assert.IsTrue(captured.ProblemDetails.Extensions.ContainsKey("traceId"),
                "Extensions must contain a 'traceId' key.");
            var traceId = captured.ProblemDetails.Extensions["traceId"] as string;
            Assert.IsFalse(string.IsNullOrEmpty(traceId), "traceId must be a non-empty string.");
        }

        [TestMethod]
        public async Task TryHandleAsync_LogsExceptionWithTraceId()
        {
            // Arrange
            var ex = new InvalidOperationException("Unique error message to verify logging.");

            // Act
            await _handler.TryHandleAsync(_context, ex, CancellationToken.None);

            // Assert: the logger received exactly one LogError call carrying the original exception
            _logger.Verify(
                l => l.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    ex,
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
                Times.Once,
                "The full exception must be logged on the server side.");
        }

        [TestMethod]
        public async Task TryHandleAsync_PrefersActivityIdOverTraceIdentifier_WhenActivityCurrentIsSet()
        {
            // Arrange: start an Activity so Activity.Current is non-null
            using var activity = new Activity("gmm-test-activity");
            activity.SetIdFormat(ActivityIdFormat.W3C);
            activity.Start();
            Assert.IsNotNull(Activity.Current, "Test precondition: Activity.Current must be non-null after Start().");
            Assert.IsFalse(string.IsNullOrEmpty(activity.Id), "Test precondition: Activity.Id must be non-empty.");

            ProblemDetailsContext? captured = null;
            _problemDetailsService
                .Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
                .Callback<ProblemDetailsContext>(c => captured = c)
                .ReturnsAsync(true);

            var ex = new Exception("boom");

            // Act
            await _handler.TryHandleAsync(_context, ex, CancellationToken.None);

            // Assert: emitted traceId equals Activity.Current.Id (not the HttpContext.TraceIdentifier fallback)
            Assert.IsNotNull(captured);
            var emittedTraceId = captured!.ProblemDetails.Extensions["traceId"] as string;
            Assert.AreEqual(activity.Id, emittedTraceId,
                "GmmExceptionHandler must prefer Activity.Current.Id over HttpContext.TraceIdentifier when both are present.");
            Assert.AreNotEqual(_context.TraceIdentifier, emittedTraceId,
                "Sanity check: the emitted traceId came from Activity, not the HttpContext fallback.");
        }

        [TestMethod]
        public async Task TryHandleAsync_WritesSanitizedFallbackBody_WhenProblemDetailsServiceReturnsFalse()
        {
            // Arrange: simulate no IProblemDetailsWriter being able to handle the response
            // (e.g. content-negotiation produced no match for the client's Accept header).
            _problemDetailsService
                .Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
                .ReturnsAsync(false);

            using var responseBody = new MemoryStream();
            _context.Response.Body = responseBody;

            var ex = new Exception("sensitive internal detail that must not leak");

            // Act
            var handled = await _handler.TryHandleAsync(_context, ex, CancellationToken.None);

            // Assert
            Assert.IsTrue(handled, "TryHandleAsync must still return true so the framework does not re-throw.");
            Assert.AreEqual(StatusCodes.Status500InternalServerError, _context.Response.StatusCode);
            Assert.AreEqual("application/problem+json", _context.Response.ContentType);

            responseBody.Position = 0;
            var body = new StreamReader(responseBody).ReadToEnd();
            Assert.IsFalse(string.IsNullOrEmpty(body), "Fallback body must not be empty.");
            Assert.IsTrue(body.Contains("\"detail\":\"An unexpected error occurred.\"", StringComparison.Ordinal),
                "Fallback body must contain the canonical sanitized detail.");
            Assert.IsTrue(body.Contains("\"traceId\":", StringComparison.Ordinal),
                "Fallback body must contain the traceId extension.");
            Assert.IsFalse(body.Contains("sensitive", StringComparison.Ordinal),
                "Fallback body must not contain any substring of the original exception message.");
        }

        [TestMethod]
        public async Task TryHandleAsync_DoesNotWriteFallback_WhenResponseHasAlreadyStarted()
        {
            // Arrange: simulate the response having already started (e.g. another middleware
            // partially wrote the response before throwing). Writing again would throw at
            // runtime; we assert here that no bytes are written to the response body when
            // HasStarted is true, even if TryWriteAsync also reports false.
            _problemDetailsService
                .Setup(p => p.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
                .ReturnsAsync(false);

            // Make HttpResponse.HasStarted observe 'true' via a fake IHttpResponseFeature.
            var responseFeature = new FakeStartedResponseFeature();
            _context.Features.Set<IHttpResponseFeature>(responseFeature);

            // Capture any bytes that would be written so we can assert the guard short-circuits.
            using var responseBody = new MemoryStream();
            _context.Response.Body = responseBody;
            var initialContentType = _context.Response.ContentType;

            var ex = new Exception("boom");

            // Act
            var handled = await _handler.TryHandleAsync(_context, ex, CancellationToken.None);

            // Assert: handler still claims responsibility but does NOT touch the body or
            // content-type because the response has already started. Removing the
            // !HasStarted guard from GmmExceptionHandler would make this test fail.
            Assert.IsTrue(handled, "TryHandleAsync must still return true so the framework does not re-throw.");
            Assert.AreEqual(0, responseBody.Length,
                "No fallback bytes must be written to the response body once HasStarted is true.");
            Assert.AreEqual(initialContentType, _context.Response.ContentType,
                "Response.ContentType must not be mutated once HasStarted is true.");
        }

        private sealed class FakeStartedResponseFeature : IHttpResponseFeature
        {
            public int StatusCode { get; set; } = StatusCodes.Status500InternalServerError;
            public string? ReasonPhrase { get; set; }
            public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
            public Stream Body { get; set; } = Stream.Null;
            public bool HasStarted => true;
            public void OnStarting(Func<object, Task> callback, object state) { }
            public void OnCompleted(Func<object, Task> callback, object state) { }
        }
    }
}
