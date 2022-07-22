// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
namespace Entities
{
    public class TableSegmentBulkResult
    {
        public AsyncPageable<SyncJob> PageableQueryResult { get; set; }
        public string ContinuationToken { get; set; }
        public List<SyncJob> Results { get; set; }
    }
}