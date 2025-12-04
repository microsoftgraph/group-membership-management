// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Hosts.MembershipAggregator
{
    public class FileDownloaderResponse
    {
        public required string FilePath { get; init; }
        public required string Content { get; init; }
    }
}
