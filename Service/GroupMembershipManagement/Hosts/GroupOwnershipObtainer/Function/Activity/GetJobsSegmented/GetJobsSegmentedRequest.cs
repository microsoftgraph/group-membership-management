// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.GroupOwnershipObtainer
{
    public class GetJobsSegmentedRequest
    {
        public string Query { get; set; }
        public string ContinuationToken { get; set; }
        public Guid? RunId { get; set; }
    }
}
