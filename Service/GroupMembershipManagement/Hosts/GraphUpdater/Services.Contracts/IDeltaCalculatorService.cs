// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Entities.ServiceBus;
using Services.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IDeltaCalculatorService
    {
        Task<DeltaResponse> CalculateDifferenceAsync(GroupMembership groupMembership, List<AzureADUser> membersFromDestinationGroup);
    }
}
