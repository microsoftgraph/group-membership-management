// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models;
using System;

namespace SqlMembershipObtainer
{
    public class JobStatusUpdaterRequest
    {
        // Nullable: a null Status means "persist auxiliary history fields only
        public required SyncStatus? Status { get; init; }
        public required SyncJob SyncJob { get; init; }
        public required int CurrentPart { get; init; }
        public required int TotalParts { get; init; }
        public Guid? AdfRunId { get; init; }
    }
}
