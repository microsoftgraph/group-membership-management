// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace Hosts.NonProdService
{
    public class SyncJobCheckerResponse
    {
        public Dictionary<int, List<Guid>> GroupSizesAndIds { get; set; }
        public Guid RunId { get; set; }
    }
}