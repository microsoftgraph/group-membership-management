// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Azure;
using Azure.Data.Tables;
using System.Collections.Generic;

namespace Entities
{
    public class TableSegmentBulkResult<T> where T : ITableEntity
    {
        public AsyncPageable<SyncJob> PageableQueryResult { get; set; }
        public string ContinuationToken { get; set; }
        public List<T> Results { get; set; }
    }
}