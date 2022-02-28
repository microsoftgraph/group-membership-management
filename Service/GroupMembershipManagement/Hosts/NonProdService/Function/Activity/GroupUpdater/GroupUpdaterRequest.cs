// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;
using System.Collections.Generic;

namespace Hosts.NonProdService
{
    public class GroupUpdaterRequest
    {
        public RequestType Type { get; set; }
        public AzureADGroup TargetGroup { get; set; }
        public ICollection<AzureADUser> Members { get; set; }
        public Guid RunId { get; set; }
    }
}