// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.WebApi.Contracts;

namespace Services.WebApi
{
    /// <summary>
    /// Applies a model-emitted operation set (set/add/remove/replace) to an inbound working query,
    /// keyed by stable partId. The server is authoritative: it enforces the safety gate, preserves
    /// unsupported/unreferenced parts, mints partIds for new parts, and reports the complete resulting
    /// query. Pure and deterministic so it can be unit-tested independently of the LLM.
    /// </summary>
    public static class CopilotOperationApplier
    {
        /// <summary>
        /// The warning returned when a full Copilot rebuild finds matching earlier group parts whose explicit
        /// identity selections do not agree. The rebuilt parts omit identityTypes so Copilot never chooses a value.
        /// </summary>
        public const string IdentitySelectionConflictWarning =
            "Some group-source identity selections were not preserved because matching earlier parts disagreed. Review those group sources in advanced JSON before saving.";

        /// <summary>
        /// Source types Copilot can manipulate. Any other type (e.g., GroupOwnership, PlaceMembership,
        /// TeamsChannelMembership) is carried into the resulting query unchanged.
        /// </summary>
        public static readonly IReadOnlySet<string> SupportedSourceTypes =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SqlMembership", "GroupMembership" };

        public static bool IsSupported(CopilotSourcePartResult part) =>
            SupportedSourceTypes.Contains(string.IsNullOrWhiteSpace(part.SourceType) ? "SqlMembership" : part.SourceType);

        /// <summary>
        /// Applies <paramref name="operations"/> to <paramref name="workingQuery"/> in order.
        /// </summary>
        /// <param name="idFactory">Factory for server-minted partIds (defaults to GUIDs). Injectable for deterministic tests.</param>
        public static CopilotOperationApplyResult Apply(
            IReadOnlyList<CopilotSourcePartResult>? workingQuery,
            IReadOnlyList<EditOperation>? operations,
            Func<string>? idFactory = null)
        {
            idFactory ??= () => Guid.NewGuid().ToString("D");
            var inbound = workingQuery?.ToList() ?? new List<CopilotSourcePartResult>();
            var ops = operations?.ToList() ?? new List<EditOperation>();
            var result = new CopilotOperationApplyResult();

            // Safety gate (atomic): every remove/replace target partId MUST exist in the inbound query
            // AND belong to a SUPPORTED part. Building knownIds from supported parts only means a
            // targeted op against an unsupported part (GroupOwnership, PlaceMembership,
            // TeamsChannelMembership) fails the gate and the whole set is rejected — preserving the
            // invariant that Copilot never deletes or mutates parts it cannot edit.
            var knownIds = new HashSet<string>(
                inbound.Where(p => IsSupported(p) && !string.IsNullOrEmpty(p.PartId)).Select(p => p.PartId),
                StringComparer.OrdinalIgnoreCase);

            int rejected = 0;
            foreach (var op in ops)
            {
                if (IsTargetedOp(op.Op) && (string.IsNullOrEmpty(op.PartId) || !knownIds.Contains(op.PartId)))
                {
                    rejected++;
                }
            }

            if (rejected > 0)
            {
                result.ErrorCode = "UnknownPartTarget";
                result.RejectedTargetCount = rejected;
                result.ResultingQuery = inbound; // unchanged
                return result;
            }

            var current = inbound.ToList();

            foreach (var op in ops)
            {
                switch ((op.Op ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "set":
                        {
                            // Replace only the SUPPORTED parts; carry all inbound unsupported parts unchanged.
                            var unsupported = current.Where(p => !IsSupported(p)).ToList();
                            var supportedNew = (op.Parts ?? new List<CopilotSourcePartResult>())
                                .Where(p => p != null)
                                .Select(p =>
                                {
                                    p.IdentityTypes = null;

                                    if (IsGroupMembership(p))
                                    {
                                        var selection = FindMatchingGroupSelection(
                                            inbound,
                                            p,
                                            out var selectionConflict);
                                        if (selectionConflict)
                                        {
                                            result.Warning = IdentitySelectionConflictWarning;
                                        }
                                        else if (selection != null)
                                        {
                                            p.IdentityTypes = new List<string>(selection);
                                        }
                                    }

                                    return Mint(p, idFactory);
                                })
                                .ToList();
                            current = supportedNew.Concat(unsupported).ToList();
                            result.PreservedUnsupportedCount += unsupported.Count;
                            result.AppliedOperations.Add(new CopilotOperationSummary { Op = "set", PartId = null });
                            break;
                        }
                    case "add":
                        {
                            if (op.Part != null)
                            {
                                op.Part.IdentityTypes = null;
                                var added = Mint(op.Part, idFactory);
                                current.Add(added);
                                result.AddCount++;
                                result.AppliedOperations.Add(new CopilotOperationSummary { Op = "add", PartId = added.PartId });
                            }
                            break;
                        }
                    case "remove":
                        {
                            var canonicalPartId = inbound
                                .First(p => string.Equals(
                                    p.PartId,
                                    op.PartId,
                                    StringComparison.OrdinalIgnoreCase))
                                .PartId;
                            current.RemoveAll(p => string.Equals(p.PartId, op.PartId, StringComparison.OrdinalIgnoreCase));
                            result.RemoveCount++;
                            result.AppliedOperations.Add(new CopilotOperationSummary { Op = "remove", PartId = canonicalPartId });
                            break;
                        }
                    case "replace":
                        {
                            var idx = current.FindIndex(p => string.Equals(p.PartId, op.PartId, StringComparison.OrdinalIgnoreCase));
                            if (idx >= 0 && op.Part != null)
                            {
                                var replacedPart = current[idx];
                                var replacement = op.Part;
                                replacement.IdentityTypes = null;
                                if (SameSourceType(replacedPart, replacement) &&
                                    replacedPart.IdentityTypes != null)
                                {
                                    replacement.IdentityTypes = new List<string>(replacedPart.IdentityTypes);
                                }
                                replacement.PartId = replacedPart.PartId;
                                current[idx] = replacement;
                                result.ReplaceCount++;
                                result.AppliedOperations.Add(new CopilotOperationSummary { Op = "replace", PartId = replacedPart.PartId });
                            }
                            break;
                        }
                    default:
                        // Unknown op: ignore (schema-validated upstream); do not mutate.
                        break;
                }
            }

            result.ResultingQuery = current;
            // Warn only when an edit actually EMPTIED a previously non-empty query. A no-op turn
            // (clarifying question / describe / off-topic) or a brand-new empty query never triggers
            // the banner. inbound>0 && current==0 implies an operation ran, so no extra guard needed.
            if (inbound.Count > 0 && current.Count == 0)
            {
                result.Warning = "This query now has no membership criteria.";
            }

            return result;
        }

        private static bool IsTargetedOp(string? op) =>
            string.Equals(op, "remove", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(op, "replace", StringComparison.OrdinalIgnoreCase);

        private static bool IsGroupMembership(CopilotSourcePartResult part) =>
            string.Equals(
                NormalizeSourceType(part.SourceType),
                "GroupMembership",
                StringComparison.OrdinalIgnoreCase);

        private static bool SameSourceType(
            CopilotSourcePartResult left,
            CopilotSourcePartResult right) =>
            string.Equals(
                NormalizeSourceType(left.SourceType),
                NormalizeSourceType(right.SourceType),
                StringComparison.OrdinalIgnoreCase);

        private static string NormalizeSourceType(string? sourceType) =>
            string.IsNullOrWhiteSpace(sourceType) ? "SqlMembership" : sourceType;

        private static IReadOnlyList<string>? FindMatchingGroupSelection(
            IReadOnlyList<CopilotSourcePartResult> inbound,
            CopilotSourcePartResult rebuiltPart,
            out bool conflict)
        {
            conflict = false;
            if (string.IsNullOrWhiteSpace(rebuiltPart.GroupId))
            {
                return null;
            }

            var matchingParts = inbound
                .Where(IsGroupMembership)
                .Where(part =>
                    !string.IsNullOrWhiteSpace(part.GroupId) &&
                    string.Equals(part.GroupId, rebuiltPart.GroupId, StringComparison.OrdinalIgnoreCase) &&
                    part.IsExclusion == rebuiltPart.IsExclusion)
                .ToList();

            if (matchingParts.Count == 0)
            {
                return null;
            }

            var earliestSelection = matchingParts[0].IdentityTypes;
            for (var index = 1; index < matchingParts.Count; index++)
            {
                if (!SelectionsAgree(earliestSelection, matchingParts[index].IdentityTypes))
                {
                    conflict = true;
                    return null;
                }
            }

            return earliestSelection == null
                ? null
                : new List<string>(earliestSelection);
        }

        private static bool SelectionsAgree(
            IReadOnlyList<string>? left,
            IReadOnlyList<string>? right)
        {
            if (left == null || right == null)
            {
                return left == null && right == null;
            }

            return IdentityTypeSet.TryResolve(
                    left,
                    IdentityTypeSet.GroupDefault,
                    IdentityTypeSet.GroupDefault,
                    out var leftSelection,
                    out _) &&
                IdentityTypeSet.TryResolve(
                    right,
                    IdentityTypeSet.GroupDefault,
                    IdentityTypeSet.GroupDefault,
                    out var rightSelection,
                    out _) &&
                leftSelection == rightSelection;
        }

        private static CopilotSourcePartResult Mint(CopilotSourcePartResult part, Func<string> idFactory)
        {
            // Server mints the partId for every newly introduced part (add / set). The model MUST NOT invent ids.
            part.PartId = idFactory();
            if (string.IsNullOrWhiteSpace(part.SourceType))
            {
                part.SourceType = "SqlMembership";
            }
            return part;
        }
    }
}
