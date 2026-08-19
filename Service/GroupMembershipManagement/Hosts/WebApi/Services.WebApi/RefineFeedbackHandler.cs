// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using System.Diagnostics;
using System.Net;
using WebApi.Models;

namespace Services.WebApi
{
    /// <summary>
    /// Refines reviewer-authored rejection feedback. This handler never submits a rejection,
    /// never changes submission review state, and never logs or persists feedback content.
    /// </summary>
    public class RefineFeedbackHandler : RequestHandlerBase<RefineFeedbackRequest, RefineFeedbackResponse>
    {
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        private readonly IFeedbackRefinementService _feedbackRefinementService;

        public RefineFeedbackHandler(
            ILogger<RefineFeedbackHandler> logger,
            IDatabaseSettingsRepository databaseSettingsRepository,
            IFeedbackRefinementService feedbackRefinementService) : base(logger)
        {
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
            _feedbackRefinementService = feedbackRefinementService ?? throw new ArgumentNullException(nameof(feedbackRefinementService));
        }

        protected override async Task<RefineFeedbackResponse> ExecuteCoreAsync(RefineFeedbackRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Feedback))
            {
                Logger.RefineFeedbackInvalidRequest(request.InstanceId, RefineFeedbackErrorCodes.InvalidRequest);
                return Failure(HttpStatusCode.BadRequest, RefineFeedbackErrorCodes.InvalidRequest, "Feedback cannot be empty.");
            }

            // Rejected before the feature check and before any provider call so that an oversized
            // payload can never reach the AI service or consume its retry budget.
            if (request.Feedback.Length > FeedbackRefinementLimits.MaxInputLength)
            {
                Logger.RefineFeedbackInvalidRequest(request.InstanceId, RefineFeedbackErrorCodes.FeedbackTooLong);
                return Failure(HttpStatusCode.BadRequest, RefineFeedbackErrorCodes.FeedbackTooLong, "Feedback exceeded the maximum length that can be refined.");
            }

            Logger.RefineFeedbackStarted(request.InstanceId, request.Feedback.Length);

            bool isEnabled;
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsAIRejectionFeedbackRefinementEnabled);
                isEnabled = setting != null
                    && bool.TryParse(setting.SettingValue, out var parsed)
                    && parsed;
            }
            catch (Exception)
            {
                Logger.RefineFeedbackSettingReadFailed(request.InstanceId);
                return Failure(HttpStatusCode.ServiceUnavailable, RefineFeedbackErrorCodes.ServiceUnavailable, "Feedback refinement is unavailable.");
            }

            if (!isEnabled)
            {
                Logger.RefineFeedbackFeatureDisabled(request.InstanceId);
                return Failure(HttpStatusCode.ServiceUnavailable, RefineFeedbackErrorCodes.FeatureDisabled, "Feedback refinement is not enabled.");
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                var refinedText = await _feedbackRefinementService.RefineAsync(request.Feedback);
                stopwatch.Stop();

                if (string.IsNullOrWhiteSpace(refinedText))
                {
                    Logger.RefineFeedbackInvalidOutput(request.InstanceId, RefineFeedbackErrorCodes.InvalidRefinedText, refinedText?.Length ?? 0, stopwatch.ElapsedMilliseconds);
                    return Failure(HttpStatusCode.UnprocessableEntity, RefineFeedbackErrorCodes.InvalidRefinedText, "Refined feedback could not be produced.");
                }

                if (refinedText.Length > FeedbackRefinementLimits.MaxRefinedTextLength)
                {
                    Logger.RefineFeedbackInvalidOutput(request.InstanceId, RefineFeedbackErrorCodes.RefinedTextTooLong, refinedText.Length, stopwatch.ElapsedMilliseconds);
                    return Failure(HttpStatusCode.UnprocessableEntity, RefineFeedbackErrorCodes.RefinedTextTooLong, "Refined feedback exceeded the maximum allowed length.");
                }

                Logger.RefineFeedbackSucceeded(request.InstanceId, request.Feedback.Length, refinedText.Length, stopwatch.ElapsedMilliseconds);

                return new RefineFeedbackResponse
                {
                    StatusCode = HttpStatusCode.OK,
                    RefinedText = refinedText
                };
            }
            catch (TimeoutException)
            {
                stopwatch.Stop();
                Logger.RefineFeedbackTimeout(request.InstanceId, stopwatch.ElapsedMilliseconds);
                return Failure(HttpStatusCode.RequestTimeout, RefineFeedbackErrorCodes.Timeout, "Feedback refinement timed out.");
            }
            catch (FeedbackRefinementUnavailableException)
            {
                stopwatch.Stop();
                Logger.RefineFeedbackUnavailable(request.InstanceId, stopwatch.ElapsedMilliseconds);
                return Failure(HttpStatusCode.ServiceUnavailable, RefineFeedbackErrorCodes.ServiceUnavailable, "Feedback refinement is unavailable.");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Only the exception type name is recorded; messages and stack detail are withheld
                // because they can echo reviewer feedback or model output.
                Logger.RefineFeedbackUnexpectedError(request.InstanceId, ex.GetType().Name, stopwatch.ElapsedMilliseconds);
                return Failure(HttpStatusCode.InternalServerError, RefineFeedbackErrorCodes.InternalError, "Feedback refinement failed.");
            }
        }

        private static RefineFeedbackResponse Failure(HttpStatusCode statusCode, string errorCode, string message)
        {
            return new RefineFeedbackResponse
            {
                StatusCode = statusCode,
                ErrorCode = errorCode,
                ResponseMessage = message
            };
        }
    }
}
