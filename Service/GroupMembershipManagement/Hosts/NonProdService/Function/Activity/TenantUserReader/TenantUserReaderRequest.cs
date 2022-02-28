// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;

namespace Hosts.NonProdService
{
    public class TenantUserReaderRequest
    {
        public int MinimunTenantUserCount { get; set; }
        public Guid RunId { get; set; }
    }
}