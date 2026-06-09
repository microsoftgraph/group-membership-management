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
            string? currentFilter = null);
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
        /// </summary>
        public List<CopilotSourcePartResult> SourceParts { get; set; } = new();

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
        public string Filter { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public bool IsExclusion { get; set; }
        public bool UseOrgStructure { get; set; }
        public string? OrgLeaderName { get; set; }
        public string? OrgLeaderEmail { get; set; }
        public string? OrgLeaderObjectId { get; set; }
        public int? OrgLeaderDepth { get; set; }
    }

    public class CopilotUserContext
    {
        public string? ManagerName { get; set; }
        public string? ManagerEmail { get; set; }
        public string? ManagerAlias { get; set; }
    }
}
