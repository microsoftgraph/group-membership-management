// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Hosts.GroupMembershipObtainer
{
    /// <summary>
    /// Identifies the Graph membership read that produced a group part.
    /// </summary>
    public enum GroupMembershipReadKind
    {
        /// <summary>
        /// A transitive-members read.
        /// </summary>
        Full,

        /// <summary>
        /// An initial group delta read.
        /// </summary>
        DeltaInitial,

        /// <summary>
        /// A group delta read that started from a saved delta link.
        /// </summary>
        DeltaLink
    }
}
