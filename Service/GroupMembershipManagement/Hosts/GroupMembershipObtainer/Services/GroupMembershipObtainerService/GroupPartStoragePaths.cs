// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// Builds the run-scoped and group-scoped blob paths used by one-type incremental reads.
    /// </summary>
    public static class GroupPartStoragePaths
    {
        /// <summary>
        /// Builds the unfiltered staging path for one group part.
        /// </summary>
        /// <param name="targetGroupId">The destination group whose upload area stores the run.</param>
        /// <param name="runId">The sync run that owns the temporary blob.</param>
        /// <param name="currentPart">The one-based query-part number.</param>
        /// <returns>The JSON blob path that holds the part's unfiltered membership snapshot.</returns>
        public static string BuildStagingPath(Guid targetGroupId, Guid runId, int currentPart) =>
            $"{targetGroupId}/userUploads/{runId}_GroupMembershipStaging_{currentPart}.json";

        /// <summary>
        /// Builds the selection folder of one read of a group part. The trailing <c>/</c> keeps part 1's folder from
        /// matching part 10's, and the run identifier is followed by <c>/</c>-terminated names, so part-file
        /// discovery, the merge prefix and the upload-cleanup prefix never match the folder's blobs.
        /// </summary>
        public static string BuildSelectionFolder(Guid targetGroupId, Guid runId, int currentPart) =>
            $"{targetGroupId}/userUploads/{runId}_GroupMembershipSelection_{currentPart}/";

        /// <summary>Builds the write-once manifest path that selects one prepare execution's snapshot.</summary>
        public static string BuildManifestPath(Guid targetGroupId, Guid runId, int currentPart) =>
            BuildSelectionFolder(targetGroupId, runId, currentPart) + "manifest.json";

        /// <summary>Builds the path of one snapshot segment written by prepare execution <paramref name="executionId"/>.</summary>
        public static string BuildSegmentPath(Guid targetGroupId, Guid runId, int currentPart, Guid executionId, int segmentIndex) =>
            BuildSelectionFolder(targetGroupId, runId, currentPart) + $"{executionId}/segment_{segmentIndex:D5}.txt";

        /// <summary>Builds the path of the write-once receipt of one segment.</summary>
        public static string BuildReceiptPath(Guid targetGroupId, Guid runId, int currentPart, Guid executionId, int segmentIndex) =>
            BuildSelectionFolder(targetGroupId, runId, currentPart) + $"{executionId}/receipt_{segmentIndex:D5}.jsonl";

        /// <summary>Builds the merge prefix of the page uploads of one read of a part.</summary>
        public static string BuildMergePrefix(Guid targetGroupId, Guid runId, int currentPart) =>
            $"{targetGroupId}/userUploads/{runId}_GroupMembership_{currentPart}_";

        /// <summary>
        /// Builds the shared recorded-type path for one source group.
        /// </summary>
        /// <param name="sourceGroupId">The source group whose current members share recorded types.</param>
        /// <returns>The JSON-lines blob path for reusable person and agent-user classifications.</returns>
        public static string BuildRecordedTypesPath(Guid sourceGroupId) =>
            $"memberTypes/{sourceGroupId}.jsonl";
    }
}
