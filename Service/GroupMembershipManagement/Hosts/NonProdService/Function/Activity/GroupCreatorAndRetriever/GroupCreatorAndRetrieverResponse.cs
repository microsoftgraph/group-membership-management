// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System.Collections.Generic;

namespace Hosts.NonProdService
{
    public class GroupCreatorAndRetrieverResponse
    {
        public AzureADGroup TargetGroup { get; set; }
        public List<AzureADUser> Members { get; set; }
    }
}