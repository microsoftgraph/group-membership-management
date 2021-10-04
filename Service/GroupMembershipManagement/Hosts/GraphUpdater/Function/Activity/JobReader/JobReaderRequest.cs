// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.GraphUpdater
{
    public class JobReaderRequest
    {
        public string JobPartitionKey { get; set; }
        public string JobRowKey { get; set; }
        public Guid RunId { get; set; }
    }
}
