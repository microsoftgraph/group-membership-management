// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using System;
using System.Collections.Generic;

namespace Hosts.NonProdService
{
    public class GroupCreatorAndRetrieverBatchRequest
    {
        public string BaseGroupName { get; set; }
        public TestGroupType TestGroupType { get; set; }
        public List<Guid> GroupOwnersIds { get; set; }
        public int GroupCount { get; set; }
        public bool RetrieveMembers { get; set; }
        public Guid RunId { get; set; }
        public List<string> ExistingGroupNames { get; set; } 
        public int StartingIndex { get; set; }
    }
}