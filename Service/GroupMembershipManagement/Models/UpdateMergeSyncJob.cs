// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Entities
{
    public class UpdateMergeSyncJob
    {
        public string PartitionKey { get; set; }

        public string RowKey { get; set; }

        public DateTime StartDate { get; set; } = DateTime.FromFileTimeUtc(0);

        public UpdateMergeSyncJob() { }
    }
}
