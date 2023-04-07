// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Repositories.LoggingRepos.Tests
{
    public class MockLoggingRepository : ILoggingRepository
    {
        private int MAX_RETRY_ATTEMPTS = 4;
        public Dictionary<Guid, LogProperties> SyncJobProperties { get; set; } = new Dictionary<Guid, LogProperties>();
        public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode FinalStatusCode { get; set; } = HttpStatusCode.OK;
        public bool PollyPolicySucceeds { get; set; } = false;
		public bool DryRun { get; set; }

        public async Task LogMessageAsync(LogMessage logMessage, VerbosityLevel verbosityLevel = VerbosityLevel.INFO, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
        {
            HttpResponseMessage response = null;

            HttpStatusCode[] httpStatusCodesWorthRetrying = {
                HttpStatusCode.RequestTimeout, // 408
                HttpStatusCode.TooManyRequests, // 429
                HttpStatusCode.InternalServerError, // 500
                HttpStatusCode.BadGateway, // 502
                HttpStatusCode.ServiceUnavailable, // 503
                HttpStatusCode.GatewayTimeout // 504
            };

            var retryPolicy = Policy
                            .Handle<HttpRequestException>()
                            .OrResult<HttpResponseMessage>(r => httpStatusCodesWorthRetrying.Contains(r.StatusCode))
                            .WaitAndRetryAsync(
                                MAX_RETRY_ATTEMPTS,
                                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                            );

            await retryPolicy.ExecuteAsync(async () =>
            {
                response = await GetHttpResponseAsync();

                if (PollyPolicySucceeds)
                    FinalStatusCode = HttpStatusCode.OK;
                else
                    FinalStatusCode = response.StatusCode;

                return response;
            });

            await Task.CompletedTask;
        }

        public async Task LogPIIMessageAsync(LogMessage logMessage, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
        {
            HttpResponseMessage response = null;

            HttpStatusCode[] httpStatusCodesWorthRetrying = {
                HttpStatusCode.RequestTimeout, // 408
                HttpStatusCode.TooManyRequests, // 429
                HttpStatusCode.InternalServerError, // 500
                HttpStatusCode.BadGateway, // 502
                HttpStatusCode.ServiceUnavailable, // 503
                HttpStatusCode.GatewayTimeout // 504
            };

            var retryPolicy = Policy
                            .Handle<HttpRequestException>()
                            .OrResult<HttpResponseMessage>(r => httpStatusCodesWorthRetrying.Contains(r.StatusCode))
                            .WaitAndRetryAsync(
                                MAX_RETRY_ATTEMPTS,
                                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                            );

            await retryPolicy.ExecuteAsync(async () =>
            {
                response = await GetHttpResponseAsync();

                if (PollyPolicySucceeds)
                    FinalStatusCode = HttpStatusCode.OK;

                else
                    FinalStatusCode = response.StatusCode;

                return response;
            });

            await Task.CompletedTask;
        }

        public async Task<HttpResponseMessage> GetHttpResponseAsync()
        {
            var mockHttpResponse = new HttpResponseMessage();
            mockHttpResponse.StatusCode = StatusCode;

            return await Task.FromResult(mockHttpResponse);
        }

        public void SetSyncJobProperties(Guid key, Dictionary<string, string> properties)
        {
            throw new NotImplementedException();
        }

        public void RemoveSyncJobProperties(Guid key)
        {
            throw new NotImplementedException();
        }
    }
}
