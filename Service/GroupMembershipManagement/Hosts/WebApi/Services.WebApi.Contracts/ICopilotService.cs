// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.WebApi.Contracts
{
    
    public interface ICopilotService
    {
        Task<CopilotChatResult> GetChatResponseAsync(
            string userMessage, 
            List<CopilotChatMessage> conversationHistory, 
            CopilotUserContext? userContext = null,
            List<CopilotSourcePartResult>? workingQuery = null,
            string? conversationId = null);
    }


    public class CopilotChatMessage
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class CopilotChatResult
    {
        public string ResponseMessage { get; set; } = string.Empty;

        /// <summary>
        /// Multiple source parts, each with its own org leader info.
        /// For single-leader requests this will have one entry; for multi-leader it will have multiple.
        /// For v1 (append semantics) this carries the newly generated parts.
        /// </summary>
        public List<CopilotSourcePartResult> SourceParts { get; set; } = new();

        /// <summary>
        /// v2 operation engine: the COMPLETE resulting query after the model's operations
        /// (set/add/remove/replace) were applied server-side to the inbound workingQuery.
        /// Each part carries a stable partId (server-minted for newly added parts). Applied by the UI by partId.
        /// </summary>
        public List<CopilotSourcePartResult> ResultingQuery { get; set; } = new();

        /// <summary>
        /// v2 operation engine: machine-readable summary of the operations applied this turn.
        /// Clients may ignore this and apply ResultingQuery directly.
        /// </summary>
        public List<CopilotOperationSummary> AppliedOperations { get; set; } = new();

        /// <summary>
        /// Soft warning surfaced to the user (e.g., the resulting query is empty). Not a block.
        /// </summary>
        public string? Warning { get; set; }

        /// <summary>
        /// Error code set when an operation set is rejected (e.g., "UnknownPartTarget"). Null on success.
        /// </summary>
        public string? ErrorCode { get; set; }

        // Convenience properties for backward compatibility (derived from first source part)
        public CopilotSourcePartResult? SourcePart => SourceParts.Count > 0 ? SourceParts[0] : null;
        public bool UseOrgStructure => SourceParts.Any(p => p.UseOrgStructure);
        public string? OrgLeaderName => SourceParts.FirstOrDefault(p => p.OrgLeaderName != null)?.OrgLeaderName;
        public string? OrgLeaderEmail => SourceParts.FirstOrDefault(p => p.OrgLeaderEmail != null)?.OrgLeaderEmail;
        public string? OrgLeaderObjectId => SourceParts.FirstOrDefault(p => p.OrgLeaderObjectId != null)?.OrgLeaderObjectId;
        public int? OrgLeaderDepth => SourceParts.FirstOrDefault(p => p.OrgLeaderDepth != null)?.OrgLeaderDepth;
    }

    public class CopilotSourcePartResult
    {
        public string PartId { get; set; } = string.Empty;
        /// <summary>
        /// "SqlMembership" (HR) or "GroupMembership". Defaults to SqlMembership for backward compatibility.
        /// </summary>
        public string SourceType { get; set; } = "SqlMembership";
        public string Filter { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsExclusion { get; set; }
        public bool UseOrgStructure { get; set; }
        public string? OrgLeaderName { get; set; }
        public string? OrgLeaderEmail { get; set; }
        public string? OrgLeaderObjectId { get; set; }
        public int? OrgLeaderDepth { get; set; }
        /// <summary>
        /// For GroupMembership source type: the Entra ID group's object ID.
        /// </summary>
        public string? GroupId { get; set; }
        /// <summary>
        /// For GroupMembership source type: the group's display name.
        /// </summary>
        public string? GroupName { get; set; }
    }

    public class CopilotUserContext
    {
        public string? ManagerName { get; set; }
        public string? ManagerEmail { get; set; }
        public string? ManagerAlias { get; set; }
    }

    /// <summary>
    /// Machine-readable summary of a single edit operation applied server-side this turn.
    /// </summary>
    public class CopilotOperationSummary
    {
        /// <summary>One of "set", "add", "remove", "replace".</summary>
        public string Op { get; set; } = string.Empty;

        /// <summary>The stable partId the operation targeted or produced (null for "set").</summary>
        public string? PartId { get; set; }
    }
}
