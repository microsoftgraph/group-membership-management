// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Models;

namespace Hosts.JobScheduler
{
    public class GetJobsSegmentedRequest
    {
        public string Query;
        public string ContinuationToken;
        public bool IncludeFutureJobs;
    }
}
