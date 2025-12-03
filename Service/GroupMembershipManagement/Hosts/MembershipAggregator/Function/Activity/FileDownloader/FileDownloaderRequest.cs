// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Hosts.MembershipAggregator
{
    public class FileDownloaderRequest
    {
        public required string FilePath { get; init; }
        public required SyncJob SyncJob { get; init; }
    }
}
