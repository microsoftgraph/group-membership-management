// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.ServiceBus;
using Services.Entities;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IDeltaCalculatorService
    {
        Task<DeltaResponse> CalculateDifferenceAsync(GroupMembership sourceMembership, GroupMembership destinationMembership);
    }
}
