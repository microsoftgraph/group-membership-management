// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace Hosts.NonProdService
{
    public class EnsureGroupOwnershipRequest
    {
        public List<Guid> ManagedGroupIds { get; set; }
        public Guid OwnerAppId { get; set; }
        public Guid RunId { get; set; }
    }
}
