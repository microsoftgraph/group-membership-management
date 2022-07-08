// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System.Collections.Generic;

namespace Services.Entities
{
    public class DeltaResponse
    {
        public ICollection<AzureADUser> MembersToAdd { get; set; }
        public ICollection<AzureADUser> MembersToRemove { get; set; }
        public MembershipDeltaStatus MembershipDeltaStatus { get; set; }
    }
}
