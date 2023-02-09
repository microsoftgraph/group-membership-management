// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class SyncJob
    {
        public string? PartitionKey { get; set; }
        public string? RowKey { get; set; }
    }
}
