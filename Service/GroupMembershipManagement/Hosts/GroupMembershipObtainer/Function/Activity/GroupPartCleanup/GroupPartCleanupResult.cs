// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.GroupMembershipObtainer
{
    public enum GroupPartCleanupStatus
    {
        /// <summary>Every listed blob was deleted and, before a replacement read, the merge prefix was confirmed empty.</summary>
        Completed,

        /// <summary>
        /// A delete failed or the merge prefix was not empty afterwards. Before a replacement
        /// read the sub-orchestrator must not start that read.
        /// </summary>
        Incomplete
    }

    /// <summary>The outcome of <see cref="GroupPartCleanupFunction"/>, recorded in orchestration history.</summary>
    public sealed class GroupPartCleanupResult
    {
        public GroupPartCleanupStatus Status { get; set; }

        /// <summary>Why the cleanup is incomplete; null when it completed.</summary>
        public string Reason { get; set; }

        public static GroupPartCleanupResult Completed() => new() { Status = GroupPartCleanupStatus.Completed };

        public static GroupPartCleanupResult Incomplete(string reason) =>
            new() { Status = GroupPartCleanupStatus.Incomplete, Reason = reason };
    }

    /// <summary>
    /// The part fails because the cleanup before a replacement read did not complete: the replacement read is not
    /// started, so no membership set is published for the part (publishing nothing is safe; publishing the abandoned
    /// read's unfiltered page uploads is not).
    /// </summary>
    public sealed class GroupPartCleanupIncompleteException : InvalidOperationException
    {
        public GroupPartCleanupIncompleteException(Guid objectId, string reason)
            : base($"Group-part cleanup before the replacement read of group {objectId} did not complete ({reason}); the replacement read was not started and the part publishes no membership set.")
        {
            Reason = reason;
        }

        public string Reason { get; }
    }
}
