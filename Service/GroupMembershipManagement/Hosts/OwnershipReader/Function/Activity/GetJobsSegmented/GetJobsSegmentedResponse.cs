// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Hosts.OwnershipReader
{
    public class GetJobsSegmentedResponse
    {
        public Page<SyncJob> ResponsePage { get; set; }
    }
}