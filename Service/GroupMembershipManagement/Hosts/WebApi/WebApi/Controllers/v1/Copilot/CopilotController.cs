// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Services.Contracts;
using Services.WebApi.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Net;

namespace WebApi.Controllers.v1.Copilot
{
    /// <summary>
    /// Copilot chat endpoint. Accepts the FULL current working query and returns the COMPLETE
    /// resulting query after the operation engine (set/add/remove/replace) is applied server-side.
    /// New-query creation and existing-query refinement share this one operation path.
    /// </summary>
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

        [Authorize(Roles = Models.Roles.AI_ONBOARDING_CHAT)]
        [HttpPost("chat")]
        [ProducesResponseType(typeof(CopilotChatResponseDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status408RequestTimeout)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> ChatAsync([FromBody] CopilotChatRequestDto request)
        {
            // Boundary validation: a missing message or a missing/null workingQuery is a clear
            // InvalidRequest — never a false "loaded" state. An empty workingQuery array is valid (new query).
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { error = "Message cannot be null or empty.", code = "InvalidRequest" });
            }

            if (request.WorkingQuery == null)
            {
                return BadRequest(new { error = "workingQuery is required (use an empty array for a new query).", code = "InvalidRequest" });
            }

            var conversationHistory = request.ConversationHistory?
                .Select(m => new CopilotChatMessage { Role = m.Role, Content = m.Content })
                .ToList() ?? new List<CopilotChatMessage>();

            var userContext = request.UserContext != null
                ? new CopilotUserContext
                {
                    ManagerName = request.UserContext.ManagerName,
                    ManagerEmail = request.UserContext.ManagerEmail,
                    ManagerAlias = request.UserContext.ManagerAlias
                }
                : null;

            var conversationId = Guid.TryParse(request.ConversationId?.Trim(), out var parsedConversationId)
                ? parsedConversationId.ToString("D")
                : null;

            var workingQuery = request.WorkingQuery.Select(MapToResult).ToList();

            var chatRequest = new CopilotChatRequest(
                request.Message,
                conversationHistory,
                userContext,
                conversationId: conversationId,
                workingQuery: workingQuery);

            var response = await _copilotChatHandler.ExecuteAsync(chatRequest);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                return Ok(new CopilotChatResponseDto
                {
                    Message = response.ResponseMessage,
                    ResultingQuery = response.ResultingQuery.Select(MapToDto).ToList(),
                    AppliedOperations = response.AppliedOperations
                        .Select(op => new OperationSummaryDto { Op = op.Op, PartId = op.PartId })
                        .ToList(),
                    Warning = response.Warning
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

        private static CopilotSourcePartResult MapToResult(SourcePartDto dto) => new()
        {
            PartId = dto.PartId,
            SourceType = string.IsNullOrWhiteSpace(dto.SourceType) ? "SqlMembership" : dto.SourceType,
            Filter = dto.Filter ?? string.Empty,
            Title = dto.Title,
            IsExclusion = dto.IsExclusion,
            UseOrgStructure = dto.UseOrgStructure,
            OrgLeaderName = dto.OrgLeaderName,
            OrgLeaderEmail = dto.OrgLeaderEmail,
            OrgLeaderObjectId = dto.OrgLeaderObjectId,
            OrgLeaderDepth = dto.OrgLeaderDepth,
            GroupId = dto.GroupId,
            GroupName = dto.GroupName
        };

        private static SourcePartDto MapToDto(CopilotSourcePartResult sp) => new()
        {
            PartId = sp.PartId,
            SourceType = sp.SourceType,
            Filter = sp.Filter,
            Title = sp.Title,
            IsExclusion = sp.IsExclusion,
            UseOrgStructure = sp.UseOrgStructure,
            OrgLeaderName = sp.OrgLeaderName,
            OrgLeaderEmail = sp.OrgLeaderEmail,
            OrgLeaderObjectId = sp.OrgLeaderObjectId,
            OrgLeaderDepth = sp.OrgLeaderDepth,
            GroupId = sp.GroupId,
            GroupName = sp.GroupName
        };
    }

    #region DTOs

    /// <summary>
    /// Chat request. Carries the FULL current working query (<see cref="WorkingQuery"/>) every turn.
    /// A brand-new query is expressed as an empty <see cref="WorkingQuery"/>.
    /// </summary>
    public class CopilotChatRequestDto
    {
        public string Message { get; set; } = string.Empty;
        public List<ChatMessageDto>? ConversationHistory { get; set; }
        public UserContextDto? UserContext { get; set; }
        public string? ConversationId { get; set; }

        /// <summary>
        /// The complete current working query. REQUIRED (may be an empty array for a new query).
        /// Each part SHOULD carry a stable GUID partId; parts of unsupported source types are preserved.
        /// </summary>
        public List<SourcePartDto>? WorkingQuery { get; set; }
    }

    public class ChatMessageDto
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class UserContextDto
    {
        public string? ManagerName { get; set; }
        public string? ManagerEmail { get; set; }
        public string? ManagerAlias { get; set; }
    }

    /// <summary>
    /// A single membership source part, in the same shape inbound and outbound.
    /// </summary>
    public class SourcePartDto
    {
        public string PartId { get; set; } = string.Empty;
        public string SourceType { get; set; } = "SqlMembership";
        public string? Filter { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool IsExclusion { get; set; }
        public bool UseOrgStructure { get; set; }
        public string? OrgLeaderName { get; set; }
        public string? OrgLeaderEmail { get; set; }
        public string? OrgLeaderObjectId { get; set; }
        public int? OrgLeaderDepth { get; set; }
        public string? GroupId { get; set; }
        public string? GroupName { get; set; }
    }

    /// <summary>
    /// Chat response. Carries the COMPLETE resulting query (not a delta) plus a machine-readable
    /// operation summary and an optional soft warning. Applied by the UI by partId.
    /// </summary>
    public class CopilotChatResponseDto
    {
        public string Message { get; set; } = string.Empty;

        /// <summary>The complete resulting query after operations were applied. Applied by the UI by partId.</summary>
        public List<SourcePartDto> ResultingQuery { get; set; } = new();

        /// <summary>Machine-readable summary of applied operations (op + partId). Clients may ignore this.</summary>
        public List<OperationSummaryDto> AppliedOperations { get; set; } = new();

        /// <summary>Set when the resulting query is empty. Soft warning, not a block.</summary>
        public string? Warning { get; set; }

        /// <summary>InvalidRequest | UnknownPartTarget | Timeout | InternalError. Null on success.</summary>
        public string? ErrorCode { get; set; }
    }

    public class OperationSummaryDto
    {
        public string Op { get; set; } = string.Empty;
        public string? PartId { get; set; }
    }

    #endregion
}
