// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Hosts.GroupOwnershipObtainer
{
    public class GetJobsSegmentedResponse
    {
        public Page<SyncJob> ResponsePage { get; set; }
    }
}