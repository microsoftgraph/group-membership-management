// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
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
        private readonly ILoggingRepository _loggingRepository;

        public CopilotChatHandler(
            ICopilotService copilotService,
            ILoggingRepository loggingRepository) : base(loggingRepository)
        {
            _copilotService = copilotService ?? throw new ArgumentNullException(nameof(copilotService));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
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

                await _loggingRepository.LogMessageAsync(new Models.LogMessage
                {
                    Message = $"CopilotChatHandler: Processing chat message"
                });

                var result = await _copilotService.GetChatResponseAsync(
                    request.UserMessage, 
                    request.ConversationHistory, 
                    request.UserContext,
                    request.CurrentFilter);

                response.StatusCode = HttpStatusCode.OK;
                response.ResponseMessage = result.ResponseMessage;
                response.SourceParts = result.SourceParts;

                await _loggingRepository.LogMessageAsync(new Models.LogMessage
                {
                    Message = $"CopilotChatHandler: Successfully generated response (SourcePart: {(result.SourcePart != null ? "included" : "none")})"
                });
            }
            catch (TimeoutException ex)
            {
                await _loggingRepository.LogMessageAsync(new Models.LogMessage
                {
                    Message = $"CopilotChatHandler: Request timed out - {ex.Message}"
                });

                response.StatusCode = HttpStatusCode.RequestTimeout;
                response.ErrorCode = "Timeout";
                response.ResponseMessage = "The request took too long to process. Please try again.";
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new Models.LogMessage
                {
                    Message = $"CopilotChatHandler: Error processing request - {ex.Message}"
                });

                response.StatusCode = HttpStatusCode.InternalServerError;
                response.ErrorCode = "InternalError";
                response.ResponseMessage = "An error occurred while processing your request. Please try again.";
            }

            return response;
        }
    }
}
