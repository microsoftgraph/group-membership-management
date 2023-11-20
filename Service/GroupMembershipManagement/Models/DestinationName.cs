// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Data.SqlTypes;

namespace Models
{
    public class DestinationName
    {
        public Guid Id { get; set; }
        public DateTime LastUpdatedTime { get; set; } = SqlDateTime.MinValue.Value;
        public string Name { get; set; }
        public SyncJob SyncJob { get; set; }
    }
}