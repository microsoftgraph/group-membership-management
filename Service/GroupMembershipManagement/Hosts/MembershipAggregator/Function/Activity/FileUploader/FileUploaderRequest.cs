// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Hosts.MembershipAggregator
{
    public class FileUploaderRequest
    {
        public required SyncJob SyncJob { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
        public required string FilePath { get; init; }

        /// <summary>
        /// Compressed serialized GroupMembership
        /// </summary>
        public required string Content { get; init; }
    }
}
