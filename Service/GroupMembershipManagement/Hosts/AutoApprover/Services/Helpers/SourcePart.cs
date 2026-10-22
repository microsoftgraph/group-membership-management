// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;
using System.Linq;

namespace Services.AutoApprover.Helpers
{
    /// <summary>
    /// A single parsed source part of an onboarding submission query. Produced by
    /// <see cref="JsonParser.TryParseParts"/> and consumed by the Per-Part Rule evaluator on
    /// <c>AutoApproverService</c>.
    /// </summary>
    internal sealed class SourcePart
    {
        public const string GroupMembershipType = "GroupMembership";
        public const string SqlMembershipType = "SqlMembership";

        public string Type { get; set; }

        // Group parts only — GUID as string.
        public string SourceGroupId { get; set; }

        // SQL parts only — from source.manager.id.
        public int? ManagerId { get; set; }

        /// <summary>SQL parts only — from <c>source.filter</c>. Whitespace-only filters become null.</summary>
        public string Filter { get; set; }

        /// <summary>
        /// Readable identifier for this part's source, surfaced on rejection logs so an operator can
        /// tell which source caused the decline without correlating adjacent traces. SQL parts are
        /// described without their manager id, which is an HR identifier.
        /// </summary>
        public string SourceDescriptor
        {
            get
            {
                if (Type == SqlMembershipType)
                    return ManagerId == null ? "filter-only source" : "manager-based source";

                return string.IsNullOrWhiteSpace(SourceGroupId) ? "(no source id)" : SourceGroupId;
            }
        }
    }

    /// <summary>
    /// The reason a single source part was rejected. Surfaced on the <c>SourcePartRejected</c>
    /// (250127) log so operators can distinguish rejection scenarios.
    /// </summary>
    internal enum SourcePartRejectionReason
    {
        Requestor_Manager_Mismatch,
        Owner_CheckFailed,
        Group_NotFound,
        Unknown_Type,
        EmployeeId_Missing,
        Group_LookupFailed,
        Owner_LookupFailed,
        Sql_NoManagerId,
        Manager_Identity_Unresolved,
        Assistant_Lookup_Unavailable,
        Requestor_Not_Assistant,

        /// <summary>A GroupMembership part whose group-based path is switched off.</summary>
        Group_Path_Disabled,

        /// <summary>A SqlMembership part with every SQL path switched off.</summary>
        Sql_Paths_Disabled,

        /// <summary>A SqlMembership part whose filter references an attribute marked Sensitive.</summary>
        Sensitive_Filter,

        /// <summary>The Sensitive attribute list could not be read, so no filter can be cleared.</summary>
        Sensitive_List_Unavailable,

        /// <summary>The configured-attribute path is on, but its configuration or the requestor's data could not be read.</summary>
        Configured_Rule_Unavailable,

        /// <summary>The requestor's attribute values do not match the configured-attribute values.</summary>
        Configured_Values_Mismatch,

        /// <summary>The requestor is not within the configured number of levels below the manager.</summary>
        Configured_Not_In_Manager_Chain
    }

    /// <summary>
    /// Why a submission's query yielded no evaluable source parts. Surfaced on the
    /// <c>PerPartQueryNotParsable</c> (250134) log so operators can tell an unsupported-but-valid
    /// GMM query shape apart from genuinely malformed JSON, without inspecting the query itself.
    /// </summary>
    internal enum QueryParseFailure
    {
        None,
        QueryEmpty,
        NotAJsonArray,
        PartNotAnObject,
        UnsupportedSourceType,
        GroupSourceNotAGroupId,
        SqlSourceMissing,
        MalformedJson,
        NoPartsInQuery
    }

    /// <summary>
    /// Why a single source part failed the Per-Part Rule, paired with a readable detail (the group's
    /// visibility, the employee id that did not match, ...) so a decline is self-explanatory in logs.
    /// </summary>
    internal readonly struct PartRejection
    {
        public PartRejection(SourcePartRejectionReason reason, string details)
            : this(reason, reason.ToString(), details)
        {
        }

        private PartRejection(SourcePartRejectionReason reason, string reasonText, string details)
        {
            Reason = reason;
            ReasonText = reasonText;
            Details = details;
        }

        public SourcePartRejectionReason Reason { get; }

        /// <summary>The reason name logged; comma-separated when several paths failed.</summary>
        public string ReasonText { get; }

        public string Details { get; }

        /// <summary>One rejection listing every failed path's reason and detail, in path order.</summary>
        public static PartRejection Combine(IReadOnlyList<PartRejection> failures) =>
            failures.Count == 1
                ? failures[0]
                : new PartRejection(failures[0].Reason,
                    string.Join(",", failures.Select(f => f.ReasonText)),
                    string.Join("; ", failures.Select(f => f.Details)));
    }

    /// <summary>
    /// Running breakdown of how each approved part passed, reported on the
    /// <c>PerPartRuleApproved</c> (250116) log when every part is approved.
    /// </summary>
    internal sealed class PartApprovalMix
    {
        // Counted separately so an operator can tell which path approved a part.
        public int SqlManagerSelf;
        public int SqlAssistant;
        public int SqlConfigured;
        public int GroupPublic;
        public int GroupOwner;
    }

    /// <summary>The admin-curated Sensitive attribute list, read at most once per submission.</summary>
    internal sealed class SensitiveAttributeCache
    {
        public bool Loaded;
        public bool Failed;
        public List<string> Names = new List<string>();
    }

    /// <summary>The configured-attribute rule's settings and chain results, cached per submission.</summary>
    internal sealed class ConfiguredAttributeState
    {
        public bool Loaded;
        public string UnavailableReason;
        public int RequestorEmployeeId;
        public bool ValuesMatch;
        public int MaxDepth;
        public Dictionary<int, bool> InManagerOrg { get; } = new();
    }

    /// <summary>Outcome of reading a leader's published assistants; only Resolved is authoritative.</summary>
    internal enum AssistantLookupStatus
    {
        Resolved,
        LeaderUnresolved,
        Unavailable
    }

    internal readonly struct AssistantLookup
    {
        public AssistantLookup(AssistantLookupStatus status, List<AzureADUser> assistants)
        {
            Status = status;
            Assistants = assistants;
        }

        public AssistantLookupStatus Status { get; }

        /// <summary>Populated only when <see cref="Status"/> is <see cref="AssistantLookupStatus.Resolved"/>.</summary>
        public List<AzureADUser> Assistants { get; }
    }
}
