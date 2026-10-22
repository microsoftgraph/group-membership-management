// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Net;
using System.Net.Http;

namespace Repositories.Contracts
{
    /// <summary>
    /// A Graph request (or a selection step built on one) still failed after its single retry owner's attempts: 429,
    /// 500/502/503/504, a transport failure, or a second HttpClient timeout. Retry exhaustion is not a fallback case:
    /// the part publishes nothing and GMO sets the job to <c>TransientError</c>, and JobTrigger picks it up again on its
    /// next tick (owner, 2026-10-21). It derives from <see cref="HttpRequestException"/> so existing handlers still see an
    /// HTTP failure; <see cref="HttpRequestException.StatusCode"/> is the last status, or null for a transport failure or
    /// timeout.
    /// </summary>
    public sealed class GraphRetriesExhaustedException : HttpRequestException
    {
        public GraphRetriesExhaustedException(string message, Exception inner = null, HttpStatusCode? statusCode = null)
            : base(message, inner, statusCode)
        {
        }

        /// <summary>The statuses the shared retry policies retry; a response still carrying one is retry exhaustion.</summary>
        public static bool IsRetryableStatus(HttpStatusCode statusCode) =>
            statusCode == HttpStatusCode.TooManyRequests ||
            statusCode == HttpStatusCode.InternalServerError ||
            statusCode == HttpStatusCode.BadGateway ||
            statusCode == HttpStatusCode.ServiceUnavailable ||
            statusCode == HttpStatusCode.GatewayTimeout;
    }
}
