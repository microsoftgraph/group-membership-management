// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Extensions.Logging;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.Contracts.Helpers;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
namespace Repositories.RetryPolicyProvider
{
    public class RetryPolicyProvider: IRetryPolicyProvider
    {
        private readonly ILogger<RetryPolicyProvider> _retryPolicyProviderLogger;
        private readonly IGraphServiceAttemptsValue _maxGraphServiceAttempts;

        public RetryPolicyProvider(ILogger<RetryPolicyProvider> retryPolicyProviderLogger, IGraphServiceAttemptsValue maxGraphServiceAttempts)
        {
            _retryPolicyProviderLogger = retryPolicyProviderLogger ?? throw new ArgumentNullException(nameof(retryPolicyProviderLogger));
            _maxGraphServiceAttempts = maxGraphServiceAttempts;
        }

        public AsyncPolicy<HttpResponseMessage> CreateRetryAfterPolicy(Guid? runId)
        {
            HttpStatusCode[] httpsStatusCodesWithRetryAfterHeader = {
                HttpStatusCode.TooManyRequests // 429
            };

            return Policy
                .HandleResult<HttpResponseMessage>(result =>
                    httpsStatusCodesWithRetryAfterHeader.Contains(result.StatusCode) && result.Headers?.RetryAfter != null)
                .WaitAndRetryAsync(
                    _maxGraphServiceAttempts.MaxRetryAfterAttempts,
                    sleepDurationProvider: GetSleepDuration,
                    onRetryAsync: async (response, timeSpan, retryCount, context) =>
                    {
                        _retryPolicyProviderLogger.LogWarningWithRunId(runId, $"Throttled by Graph for the timespan: {timeSpan}. The retry count is {retryCount}.");

                        await Task.CompletedTask;
                    });
        }

        public AsyncPolicy<HttpResponseMessage> CreateExceptionHandlingPolicy(Guid? runId)
        {
            HttpStatusCode[] httpStatusCodesWorthRetryingExponentially = {
                HttpStatusCode.InternalServerError, // 500
                HttpStatusCode.BadGateway, // 502
                HttpStatusCode.ServiceUnavailable, // 503
                HttpStatusCode.GatewayTimeout // 504
            };

            return Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(r => httpStatusCodesWorthRetryingExponentially.Contains(r.StatusCode))
                .WaitAndRetryAsync(
                    _maxGraphServiceAttempts.MaxExceptionHandlingAttempts,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetryAsync: async (timeSpan, retryCount, context) =>
                    {
                        _retryPolicyProviderLogger.LogInformationWithRunId(runId, $"Exponential backoff {retryCount}.");

                        await Task.CompletedTask;
                    });
        }

        private TimeSpan GetSleepDuration(int retryCount, DelegateResult<HttpResponseMessage> response, Context context)
        {
            var waitTime = response.Result.Headers.RetryAfter.Date.Value - DateTime.UtcNow;

            _retryPolicyProviderLogger.LogInformation($"Wait time set to {waitTime}");

            return waitTime;
        }
    }
}
