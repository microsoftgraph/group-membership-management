// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.ComponentModel.DataAnnotations;

namespace Entities
{
    public class SyncJobChange
    {
        public Guid Id { get; set; }
        public Guid SyncJobId { get; set; }
        public DateTime ChangeTime { get; set; } = DateTime.UtcNow;
        public string ChangedByDisplayName { get; set; }
        public Guid ChangedByObjectId { get; set; }
        public SyncJobChangeSource ChangeSource { get; set; }
        public string ChangeReason { get; set; }
        public string ChangeDetails { get; set; }
    }
}
