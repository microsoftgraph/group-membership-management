// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Data.SqlTypes;

namespace Models
{
    public class DestinationOwner
    {
        public Guid Id { get; set; }
        public DateTime LastUpdatedTime { get; set; } = SqlDateTime.MinValue.Value;
        public Guid ObjectId { get; set; }
        public List<SyncJob> SyncJobs { get; set; }
        
    }
}
