// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models;

namespace SqlMembershipObtainer
{
    public class JobStatusUpdaterRequest
    {
        public required SyncStatus Status { get; init; }
        public required SyncJob SyncJob { get; init; }
    }
}
