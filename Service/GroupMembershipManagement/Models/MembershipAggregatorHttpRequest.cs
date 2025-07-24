// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Models
{
    public class MembershipAggregatorHttpRequest
    {
        public required string FilePath { get; init; }
        public required int PartNumber { get; init; }
        public required int PartsCount { get; init; }
        public required SyncJob SyncJob { get; init; }
        public required bool IsDestinationPart { get; init; }
    }
}
