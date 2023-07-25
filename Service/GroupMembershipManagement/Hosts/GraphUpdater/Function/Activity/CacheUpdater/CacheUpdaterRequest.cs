// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public class CacheUpdaterRequest
    {
        public Guid GroupId { get; set; }
        public List<AzureADUser> UserIds { get; set; }
        public string FileContent { get; set; }
        public Guid? RunId { get; set; }
        public string Timestamp { get; set; }
    }
}