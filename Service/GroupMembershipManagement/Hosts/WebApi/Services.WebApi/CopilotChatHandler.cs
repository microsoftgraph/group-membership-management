// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.Extensions.Logging;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using System.Net;

namespace Services.WebApi
{
    public class CopilotChatHandler : RequestHandlerBase<CopilotChatRequest, CopilotChatResponse>
    {
        private readonly ICopilotService _copilotService;
        private readonly ILogger<CopilotChatHandler> _logger;

        public CopilotChatHandler(
            ILogger<CopilotChatHandler> logger,
            ICopilotService copilotService) : base(logger)
        {
            _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task<CopilotChatResponse> ExecuteCoreAsync(CopilotChatRequest request)
        {
            var response = new CopilotChatResponse();

            try
            {
                if (string.IsNullOrWhiteSpace(request.UserMessage))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    response.ErrorCode = "InvalidRequest";
                    response.ResponseMessage = "User message cannot be empty.";
                    return response;
                }

                _logger.CopilotChatProcessing();

                var result = await _copilotService.GetChatResponseAsync(
                    request.UserMessage, 
                    request.ConversationHistory, 
                    request.UserContext,
                    request.WorkingQuery,
                    request.ConversationId);

                // Atomic reject from the operation engine (e.g., a remove/replace targeting an
                // unknown partId) surfaces as a 400 with the unchanged query.
                if (!string.IsNullOrEmpty(result.ErrorCode))
                {
                    response.StatusCode = HttpStatusCode.BadRequest;
                    response.ErrorCode = result.ErrorCode;
                    response.ResponseMessage = string.IsNullOrEmpty(result.ResponseMessage)
                        ? "The requested change targeted a part that is not part of the current query."
                        : result.ResponseMessage;
                    response.SourceParts = result.SourceParts;
                    response.ResultingQuery = result.ResultingQuery;
                    response.AppliedOperations = result.AppliedOperations;
                    return response;
                }

                response.StatusCode = HttpStatusCode.OK;
                response.ResponseMessage = result.ResponseMessage;
                response.SourceParts = result.SourceParts;
                response.ResultingQuery = result.ResultingQuery;
                response.AppliedOperations = result.AppliedOperations;
                response.Warning = result.Warning;

                // Report whether any resulting parts were produced (was previously a misleading
                // convenience-property check that misreported when parts came from a different parse path).
                var partsProduced = (result.ResultingQuery?.Count ?? 0) > 0 || result.SourceParts.Count > 0;
                _logger.CopilotChatResponseGenerated(partsProduced);
            }
            catch (TimeoutException ex)
            {
                _logger.CopilotChatTimeout(ex);

                response.StatusCode = HttpStatusCode.RequestTimeout;
                response.ErrorCode = "Timeout";
                response.ResponseMessage = "The request took too long to process. Please try again.";
            }
            catch (Exception ex)
            {
                _logger.CopilotChatFailed(ex);

                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorCode = "InternalError";
                response.ResponseMessage = "An error occurred while processing your request. Please try again.";
            }

            return response;
        }
    }
}
