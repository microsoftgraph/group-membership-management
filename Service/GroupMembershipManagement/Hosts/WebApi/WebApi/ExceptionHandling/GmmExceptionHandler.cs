// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.ExceptionHandling
{
    /// <summary>
    /// Safety-net <see cref="IExceptionHandler"/> for any exception that escapes the
    /// per-action <c>try/catch</c> blocks. Logs the full exception server-side and
    /// returns a sanitized <see cref="ProblemDetails"/> body to the client with a
    /// <c>traceId</c> extension for correlation.
    /// </summary>
    public sealed class GmmExceptionHandler : IExceptionHandler
    {
        private const string GenericDetail = "An unexpected error occurred.";
        private const string GenericTitle = "An error occurred while processing your request.";

        private readonly ILogger<GmmExceptionHandler> _logger;
        private readonly IProblemDetailsService _problemDetailsService;

        public GmmExceptionHandler(
            ILogger<GmmExceptionHandler> logger,
            IProblemDetailsService problemDetailsService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _problemDetailsService = problemDetailsService ?? throw new ArgumentNullException(nameof(problemDetailsService));
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

            _logger.LogError(
                exception,
                "Unhandled exception for {Method} {Path}; traceId={TraceId}",
                httpContext.Request.Method,
                httpContext.Request.Path,
                traceId);

            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

            var problemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = GenericTitle,
                Detail = GenericDetail,
            };
            problemDetails.Extensions["traceId"] = traceId;

            await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails = problemDetails,
                Exception = exception,
            });

            return true;
        }
    }
}
