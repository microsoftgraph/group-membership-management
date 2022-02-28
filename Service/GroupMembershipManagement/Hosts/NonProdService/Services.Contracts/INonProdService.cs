// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface INonProdService
    {
        MembershipDifference GetMembershipDifference(AzureADGroup group, List<AzureADUser> currentMembership, List<AzureADUser> targetMembership, Guid runId);
    }
}
