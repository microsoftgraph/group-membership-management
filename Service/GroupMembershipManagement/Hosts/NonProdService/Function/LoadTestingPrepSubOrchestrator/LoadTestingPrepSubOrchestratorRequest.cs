// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.NonProdService
{
    internal class LoadTestingPrepSubOrchestratorRequest
    {
        public Guid RunId { get; set; }
        public int TenantUserCount { get; set; }
    }
}