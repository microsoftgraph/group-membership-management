// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Services.Entities;
using System;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public class CacheUpdaterRequest : GraphUpdaterRequestBase
    {
        public Guid GroupId { get; set; }
        public HashSet<Guid> UserIds { get; set; }
        public DateTime Timestamp { get; set; }
        public string CacheFilePath { get; set; }
    }
}