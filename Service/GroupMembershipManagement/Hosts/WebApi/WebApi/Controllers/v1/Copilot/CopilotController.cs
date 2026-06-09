// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi.Contracts;
using System.Net;
using System.Text.Json;

namespace WebApi.Controllers.v1.Copilot
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/Copilot")]
    public class CopilotController : ControllerBase
    {
        private readonly IRequestHandler<CopilotChatRequest, CopilotChatResponse> _copilotChatHandler;

        public CopilotController(
            IRequestHandler<CopilotChatRequest, CopilotChatResponse> copilotChatHandler)
        {
            _copilotChatHandler = copilotChatHandler ?? throw new ArgumentNullException(nameof(copilotChatHandler));
        }

        [Authorize()]
        [HttpPost("chat")]
        [ProducesResponseType(typeof(CopilotChatResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status408RequestTimeout)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ChatAsync([FromBody] CopilotChatRequestDto request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { error = "Message cannot be null or empty." });
            }

            var conversationHistory = request.ConversationHistory?
                .Select(m => new CopilotChatMessage
                {
                    Role = m.Role,
                    Content = m.Content
                })
                .ToList() ?? new List<CopilotChatMessage>();

            var userContext = request.UserContext != null
                ? new CopilotUserContext
                {
                    ManagerName = request.UserContext.ManagerName,
                    ManagerEmail = request.UserContext.ManagerEmail,
                    ManagerAlias = request.UserContext.ManagerAlias
                }
                : null;

            var chatRequest = new CopilotChatRequest(request.Message, conversationHistory, userContext, request.CurrentFilter);
            var response = await _copilotChatHandler.ExecuteAsync(chatRequest);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                return Ok(new CopilotChatResponseDto
                {
                    Message = response.ResponseMessage,
                    SourceParts = response.SourceParts.Select(sp => new SourcePartDto
                    {
                        PartId = sp.PartId,
                        Filter = sp.Filter,
                        Title = sp.Title,
                        IsExclusion = sp.IsExclusion,
                        UseOrgStructure = sp.UseOrgStructure,
                        OrgLeaderName = sp.OrgLeaderName,
                        OrgLeaderEmail = sp.OrgLeaderEmail,
                        OrgLeaderObjectId = sp.OrgLeaderObjectId,
                        OrgLeaderDepth = sp.OrgLeaderDepth
                    }).ToList()
                });
            }

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                return BadRequest(new { error = response.ResponseMessage, code = response.ErrorCode });
            }

            if (response.StatusCode == HttpStatusCode.RequestTimeout)
            {
                return StatusCode(StatusCodes.Status408RequestTimeout, new { error = response.ResponseMessage, code = response.ErrorCode });
            }

            return StatusCode(StatusCodes.Status500InternalServerError, new { error = response.ResponseMessage, code = response.ErrorCode });
        }
    }

    #region DTOs

    public class CopilotChatRequestDto
    {
        public string Message { get; set; } = string.Empty;
        public List<ChatMessageDto>? ConversationHistory { get; set; }
        public UserContextDto? UserContext { get; set; }
        public string? CurrentFilter { get; set; }
    }

    public class ChatMessageDto
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO for the chat response to the frontend.
    /// Includes the message and optional sourceParts array (supports multiple org leaders).
    /// </summary>
    public class CopilotChatResponseDto
    {
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Array of generated source parts, each with its own org leader info.
        /// Empty if no filter was generated (e.g., clarifying question, off-topic).
        /// </summary>
        public List<SourcePartDto> SourceParts { get; set; } = new();
    }

    public class SourcePartDto
    {
        public string PartId { get; set; } = string.Empty;
        public string Filter { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsExclusion { get; set; }
        public bool UseOrgStructure { get; set; }
        public string? OrgLeaderName { get; set; }
        public string? OrgLeaderEmail { get; set; }
        public string? OrgLeaderObjectId { get; set; }
        public int? OrgLeaderDepth { get; set; }
    }

    public class UserContextDto
    {
        public string? ManagerName { get; set; }
        public string? ManagerEmail { get; set; }
        public string? ManagerAlias { get; set; }
    }

    #endregion
}
