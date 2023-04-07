// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Collections.Generic;

namespace Hosts.JobTrigger
{
    public class GetJobsSegmentedResponse
    {
        public string Query { get; set; }
        public string ContinuationToken { get; set; }
        public IReadOnlyList<SyncJob> JobsSegment { get; set; }

    }
}