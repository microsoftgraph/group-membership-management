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
                    request.CurrentFilter,
                    request.ConversationId);

                response.StatusCode = HttpStatusCode.OK;
                response.ResponseMessage = result.ResponseMessage;
                response.SourceParts = result.SourceParts;

                _logger.CopilotChatResponseGenerated(result.SourcePart != null);
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
