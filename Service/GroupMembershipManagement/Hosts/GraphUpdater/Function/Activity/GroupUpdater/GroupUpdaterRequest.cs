// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;
using System.Collections.Generic;

namespace Hosts.GraphUpdater
{
    public class GroupUpdaterRequest
    {
        public Guid RunId { get; set; }
        public Guid DestinationGroupId { get; set; }
        public RequestType Type { get; set; }
        public ICollection<AzureADUser> Members { get; set; }
    }
}