// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Polly;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
namespace Repositories.RetryPolicyProvider
{
    public class RetryPolicyProvider: IRetryPolicyProvider
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphServiceAttemptsValue _maxGraphServiceAttempts;

        public RetryPolicyProvider(ILoggingRepository loggingRepository, IGraphServiceAttemptsValue maxGraphServiceAttempts)
        {
            _loggingRepository = loggingRepository;
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
                        await _loggingRepository.LogMessageAsync(new LogMessage
                        {
                            Message = $"Throttled by Graph for the timespan: {timeSpan}. The retry count is {retryCount}.",
                            RunId = runId
                        });
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
                        await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Exponential backoff {retryCount}.", RunId = runId });
                    });
        }

        private TimeSpan GetSleepDuration(int retryCount, DelegateResult<HttpResponseMessage> response, Context context)
        {
            var waitTime = response.Result.Headers.RetryAfter.Date.Value - DateTime.UtcNow;

            _ = _loggingRepository.LogMessageAsync(new LogMessage
            {
                Message = $"Wait time set to {waitTime}",
                RunId = null
            });

            return waitTime;
        }
    }
}