// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

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

            // Safety gate (atomic): every remove/replace target partId MUST exist in the inbound query.
            // If any is unknown, reject the whole set — apply nothing, return the unchanged query.
            var knownIds = new HashSet<string>(
                inbound.Where(p => !string.IsNullOrEmpty(p.PartId)).Select(p => p.PartId),
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
                                .Select(p => Mint(p, idFactory))
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
                                var added = Mint(op.Part, idFactory);
                                current.Add(added);
                                result.AddCount++;
                                result.AppliedOperations.Add(new CopilotOperationSummary { Op = "add", PartId = added.PartId });
                            }
                            break;
                        }
                    case "remove":
                        {
                            current.RemoveAll(p => string.Equals(p.PartId, op.PartId, StringComparison.OrdinalIgnoreCase));
                            result.RemoveCount++;
                            result.AppliedOperations.Add(new CopilotOperationSummary { Op = "remove", PartId = op.PartId });
                            break;
                        }
                    case "replace":
                        {
                            var idx = current.FindIndex(p => string.Equals(p.PartId, op.PartId, StringComparison.OrdinalIgnoreCase));
                            if (idx >= 0 && op.Part != null)
                            {
                                var replacement = op.Part;
                                replacement.PartId = op.PartId!; // keep the same stable id
                                current[idx] = replacement;
                                result.ReplaceCount++;
                                result.AppliedOperations.Add(new CopilotOperationSummary { Op = "replace", PartId = op.PartId });
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
