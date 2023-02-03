// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;

namespace Hosts.JobTrigger
{
    public class GroupVerifierRequest
    {
        public SyncJob SyncJob { get; set; }
        public string FunctionDirectory { get; set; }
    }
}
