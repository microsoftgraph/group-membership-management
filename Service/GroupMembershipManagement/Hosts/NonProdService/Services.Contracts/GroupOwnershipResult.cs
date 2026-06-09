// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;

namespace Services.Contracts
{
    public class GroupOwnershipResult
    {
        public int TotalManagedGroups { get; set; }
        public int GroupsAlreadyOwned { get; set; }
        public int GroupsNewlyOwned { get; set; }
        public int GroupsFailed { get; set; }
        public List<Guid> FailedGroupIds { get; set; }
    }
}
