// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace NonProdService.LoadTestingPrepSubOrchestrator
{
    public class LoadTestingPrepSubOrchestratorOptions
    {
        public Guid DestinationGroupOwnerId { get; set; }
        public int GroupCount { get; set; }
    }
}
