// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace Hosts.NonProdService
{
    public class SyncJobCheckerRequest
    {
        public List<Guid> TargetGroupIds { get; set; }
        public Guid RunId { get; set; }
        public Dictionary<int, int> ExpectedTargetDistribution { get; set; }
    }
}