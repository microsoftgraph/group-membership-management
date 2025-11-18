// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models; // LogMessage
using Repositories.Contracts;
using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Common.DependencyInjection
{
    /// <summary>
    /// DelegatingHandler that logs only Microsoft Graph requests related to transitive members:
    ///  - /groups/{id}/transitiveMembers
    ///  - /groups/{id}/transitiveMembers/graph.user/$count
    ///  - /groups/{id}/transitiveMembers/graph.group/$count
    /// Next page requests are captured because OData nextLink retains /transitiveMembers.
    /// </summary>
    internal sealed class TransitiveMembersLoggingHandler : DelegatingHandler
    {
        private readonly ILoggingRepository _loggingRepository;

        public TransitiveMembersLoggingHandler(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (IsTransitiveMembersRequest(request.RequestUri))
            {
                var runId = ExtractRunId();
                HttpResponseMessage response = null;

                try
                {
                    response = await base.SendAsync(request, cancellationToken);
                }
                catch (Exception ex)
                {
                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        RunId = runId,
                        Message = $"Unexpected error: {ex.Message}",
                    });

                    throw;
                }

                if (_loggingRepository != null)
                {
                    string requestId = null;
                    if (response.Headers.TryGetValues("request-id", out var requestIds))
                    {
                        requestId = requestIds.FirstOrDefault();
                    }

                    Activity.Current?.SetTag("GraphRequestId", requestId);

                    await _loggingRepository.LogMessageAsync(new LogMessage
                    {
                        RunId = runId,
                        Message = $"Graph GET {request.RequestUri.AbsolutePath} StatusCode: {(int)response.StatusCode} ",
                        DynamicProperties = requestId != null ? new Dictionary<string, string> { { "GraphRequestId", requestId } } : null
                    }, VerbosityLevel.DEBUG);
                }
                return response;
            }

            return await base.SendAsync(request, cancellationToken);
        }

        private static bool IsTransitiveMembersRequest(Uri uri)
        {
            if (uri == null) return false;
            var path = uri.AbsolutePath.ToLowerInvariant();
            return path.Contains("/transitivemembers");
        }

        private Guid? ExtractRunId()
        {
            // Read from Activity baggage/tag only
            var baggage = Activity.Current?.GetBaggageItem("RunId");
            if (Guid.TryParse(baggage, out var parsed)) return parsed;
            var tag = Activity.Current?.Tags.FirstOrDefault(t => t.Key == "RunId").Value;
            if (Guid.TryParse(tag, out parsed)) return parsed;
            return null;
        }
    }
}
