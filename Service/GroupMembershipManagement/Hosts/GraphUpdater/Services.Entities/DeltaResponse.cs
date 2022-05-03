// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;
using System.Collections.Generic;

namespace Services.Entities
{
    public class DeltaResponse
    {
        public ICollection<AzureADUser> MembersToAdd { get; set; }
        public ICollection<AzureADUser> MembersToRemove { get; set; }
        public GraphUpdaterStatus GraphUpdaterStatus { get; set; }
        public SyncStatus SyncStatus { get; set; }
        public bool IsInitialSync { get; set; }
        public bool IsDryRunSync { get; set; }
        public string Requestor { get; set; }
        public DateTimeOffset Timestamp { get; set; }
    }
}
