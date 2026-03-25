// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Hosts.MembershipAggregator
{
    public class FileDeleterRequest
    {
        public required SyncJob SyncJob { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
        public required string FilePath { get; init; }
    }
}