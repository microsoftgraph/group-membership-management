// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Services.WebApi.Validators
{
    /// <summary>
    /// The outcome of validating a job's TeamsChannel source parts. A null result means the job is
    /// valid (or has no TeamsChannel source part); otherwise the error code/message describe why the
    /// job must be rejected with a 400.
    /// </summary>
    public sealed record TeamsChannelSourceValidationError(string ErrorCode, string Message);

    /// <summary>
    /// Validates the US3 rules for a TeamsChannel membership source part: it is only permitted when it
    /// references the same channel as this job's TeamsChannel destination, the job has at least one other
    /// source part of a recognized type, and the part is inclusionary. This is shared by both the create
    /// (PostJobHandler) and update (PatchJobHandler) paths so an edit cannot persist a query the create
    /// path would have rejected.
    /// </summary>
    public static class TeamsChannelSourcePartValidator
    {
        // The source-part "type" values GMM knows how to process (mirrors the UI SourcePartType enum and
        // the advanced-view Query.json schema). A TeamsChannel source must be combined with an additional
        // part of one of these types; a part with a blank or unrecognized type does not count, because no
        // membership obtainer would process it and the sync would never complete.
        private static readonly HashSet<string> KnownNonTeamsChannelSourceTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "GroupMembership",
                "SqlMembership",
                "GroupOwnership",
                "PlaceMembership"
            };

        public static TeamsChannelSourceValidationError? Validate(SyncJob syncJob)
        {
            var sourceParts = ParseQuerySourceParts(syncJob.Query);
            var teamsChannelSourceParts = sourceParts
                .Where(IsTeamsChannelSourcePart)
                .ToList();

            if (teamsChannelSourceParts.Count == 0)
            {
                return null;
            }

            // A TeamsChannel source part requires a TeamsChannel destination on the same job.
            var destinationChannelId = GetDestinationChannelId(syncJob);
            if (syncJob.MembershipType != MembershipTypes.TeamsChannelMembership.ToString()
                || string.IsNullOrEmpty(destinationChannelId))
            {
                return new TeamsChannelSourceValidationError(
                    "TeamsChannelSourceRequiresChannelDestination",
                    "A TeamsChannel source is only allowed on a job whose destination is a Teams channel.");
            }

            var destinationGroupId = syncJob.TargetOfficeGroupId.ToString();

            // Each TeamsChannel source part must reference the same channel as the destination.
            if (teamsChannelSourceParts.Any(p =>
                    !string.Equals(p.SourceObjectId, destinationGroupId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(p.SourceChannelId, destinationChannelId, StringComparison.OrdinalIgnoreCase)))
            {
                return new TeamsChannelSourceValidationError(
                    "TeamsChannelSourceMustMatchDestination",
                    "A TeamsChannel source must reference the same channel as this job's destination.");
            }

            // At least one other source part with a recognized GMM type must be present. Typeless or
            // unrecognized parts do not count as an additional source: nothing would process them and
            // the sync could never finish.
            if (!sourceParts.Any(p => p.Type != null
                    && !IsTeamsChannelSourcePart(p)
                    && KnownNonTeamsChannelSourceTypes.Contains(p.Type)))
            {
                return new TeamsChannelSourceValidationError(
                    "TeamsChannelSourceRequiresAdditionalSource",
                    "A TeamsChannel source must be combined with at least one other source part.");
            }

            // A TeamsChannel source part is inclusionary only.
            if (teamsChannelSourceParts.Any(p => p.Exclusionary))
            {
                return new TeamsChannelSourceValidationError(
                    "TeamsChannelSourceCannotBeExclusionary",
                    "A TeamsChannel source cannot be exclusionary.");
            }

            return null;
        }

        private static bool IsTeamsChannelSourcePart(SourcePartInfo part) =>
            string.Equals(part.Type, MembershipTypes.TeamsChannelMembership.ToString(), StringComparison.OrdinalIgnoreCase);

        // Resolves the destination channel id from the hydrated Channel navigation when present (the create
        // path), otherwise from the stored Destination JSON (the update path may not hydrate Channel).
        private static string? GetDestinationChannelId(SyncJob syncJob)
        {
            if (!string.IsNullOrEmpty(syncJob.Channel?.ChannelId))
            {
                return syncJob.Channel.ChannelId;
            }

            if (string.IsNullOrWhiteSpace(syncJob.Destination))
            {
                return null;
            }

            try
            {
                var destinationArray = JsonSerializer.Deserialize<List<JsonElement>>(syncJob.Destination);
                var destination = destinationArray?.FirstOrDefault();
                if (destination != null
                    && destination.Value.TryGetProperty("value", out var value)
                    && value.TryGetProperty("channelId", out var channelIdElement))
                {
                    return channelIdElement.GetString();
                }
            }
            catch (JsonException)
            {
                // A malformed Destination is treated as "no channel destination" and rejected above.
            }

            return null;
        }

        private static List<SourcePartInfo> ParseQuerySourceParts(string? query)
        {
            var parts = new List<SourcePartInfo>();
            if (string.IsNullOrWhiteSpace(query))
            {
                return parts;
            }

            if (JsonNode.Parse(query) is not JsonArray queryArray)
            {
                return parts;
            }

            foreach (var item in queryArray)
            {
                if (item is not JsonObject sourcePart)
                {
                    continue;
                }

                var info = new SourcePartInfo
                {
                    Type = ReadStringNode(sourcePart["type"])
                };

                if (sourcePart["exclusionary"] is JsonValue exclusionaryValue)
                {
                    if (exclusionaryValue.TryGetValue<bool>(out var exclusionary))
                    {
                        info.Exclusionary = exclusionary;
                    }
                    else if (exclusionaryValue.TryGetValue<string>(out var exclusionaryText)
                        && bool.TryParse(exclusionaryText, out var parsedExclusionary))
                    {
                        // Align with the trigger's (bool) cast: a stringified boolean must be honored here
                        // so an exclusionary TeamsChannel source is rejected at submit rather than crashing later.
                        info.Exclusionary = parsedExclusionary;
                    }
                }

                if (sourcePart["source"] is JsonObject source)
                {
                    info.SourceObjectId = ReadStringNode(source["objectId"]);
                    info.SourceChannelId = ReadStringNode(source["channelId"]);
                }

                parts.Add(info);
            }

            return parts;
        }

        // Returns the node's string value, or null when it is absent or not a JSON string. This keeps
        // the parser tolerant of malformed parts so validation rules (not an exception) reject them.
        private static string? ReadStringNode(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        private sealed class SourcePartInfo
        {
            public string? Type { get; set; }
            public bool Exclusionary { get; set; }
            public string? SourceObjectId { get; set; }
            public string? SourceChannelId { get; set; }
        }
    }
}
