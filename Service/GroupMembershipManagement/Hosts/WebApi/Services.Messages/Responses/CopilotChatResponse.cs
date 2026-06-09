// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Responses;
using Services.WebApi.Contracts;
using System.Net;

namespace Services.Messages.Responses
{
    public class CopilotChatResponse : ResponseBase
    {

        public HttpStatusCode StatusCode { get; set; }

        public string ResponseMessage { get; set; } = string.Empty;

        /// <summary>
        /// Multiple source parts, each with its own org leader info.
        /// </summary>
        public List<CopilotSourcePartResult> SourceParts { get; set; } = new();

        // Backward-compat convenience properties
        public CopilotSourcePartResult? SourcePart => SourceParts.Count > 0 ? SourceParts[0] : null;
        public bool UseOrgStructure => SourceParts.Any(p => p.UseOrgStructure);
        public string? OrgLeaderName => SourceParts.FirstOrDefault(p => p.OrgLeaderName != null)?.OrgLeaderName;
        public string? OrgLeaderEmail => SourceParts.FirstOrDefault(p => p.OrgLeaderEmail != null)?.OrgLeaderEmail;
        public string? OrgLeaderObjectId => SourceParts.FirstOrDefault(p => p.OrgLeaderObjectId != null)?.OrgLeaderObjectId;
        public int? OrgLeaderDepth => SourceParts.FirstOrDefault(p => p.OrgLeaderDepth != null)?.OrgLeaderDepth;

        public string? ErrorCode { get; set; }
    }
}
