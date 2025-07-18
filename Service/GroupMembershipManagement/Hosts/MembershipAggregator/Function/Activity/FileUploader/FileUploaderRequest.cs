// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Hosts.MembershipAggregator
{
    public class FileUploaderRequest
    {
        public required string FilePath { get; init; }

        /// <summary>
        /// Compressed serialized GroupMembership
        /// </summary>
        public required string Content { get; init; }
        public required SyncJob SyncJob { get; init; }
    }
}
