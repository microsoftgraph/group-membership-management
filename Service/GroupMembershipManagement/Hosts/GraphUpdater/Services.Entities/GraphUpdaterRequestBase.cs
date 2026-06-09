// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;

namespace Services.Entities
{
    public class GraphUpdaterRequestBase
    {
        public SyncJob SyncJob { get; set; }
        public int? MessageIndex { get; set; }
        public int? TotalMessageCount { get; set; }
        public string Instance { get; set; }
    }
}
