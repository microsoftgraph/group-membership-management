// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.NonProdService
{
    internal class IntegrationTestingPrepSubOrchestratorRequest
    {
        public Guid RunId { get; set; }
        public int TenantUserCount { get; set; }
    }
}