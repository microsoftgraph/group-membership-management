// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Entities
{
    public class GraphUpdaterHttpRequest
    {
        public string FilePath { get; set; }
        public Guid RunId { get; set; }
        public string JobPartitionKey { get; set; }
        public string JobRowKey { get; set; }
    }
}
