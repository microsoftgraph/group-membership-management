// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts
{
    /// <summary>
    /// Describes the directory objects returned by one completed Graph membership-reader call.
    /// </summary>
    public sealed class MemberReadCounts
    {
        /// <summary>
        /// Gets or sets the number of selected people and agent users contributed as members.
        /// </summary>
        public int MemberCount { get; set; }

        /// <summary>
        /// Gets or sets the number of people returned, or <see langword="null"/> when the read does not classify
        /// user-family members.
        /// </summary>
        public int? UserCount { get; set; }

        /// <summary>
        /// Gets or sets the number of agent users returned, or <see langword="null"/> when the read does not
        /// classify user-family members.
        /// </summary>
        public int? AgentCount { get; set; }

        /// <summary>
        /// Gets or sets the number of service principals and agent identities skipped.
        /// </summary>
        public int ServicePrincipalCount { get; set; }

        /// <summary>
        /// Gets or sets the number of devices skipped.
        /// </summary>
        public int DeviceCount { get; set; }

        /// <summary>
        /// Gets or sets the number of organizational contacts skipped.
        /// </summary>
        public int ContactCount { get; set; }

        /// <summary>
        /// Gets or sets the number of groups skipped.
        /// </summary>
        public int GroupCount { get; set; }

        /// <summary>
        /// Gets or sets the number of entries skipped because Graph supplied no usable directory-object type.
        /// </summary>
        public int UnknownTypeCount { get; set; }

        /// <summary>
        /// Gets or sets the number of entries skipped with any other or unrecognized Graph label.
        /// </summary>
        public int OtherCount { get; set; }
    }
}
