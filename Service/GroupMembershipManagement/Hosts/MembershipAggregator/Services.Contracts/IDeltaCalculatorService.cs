// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities.ServiceBus;
using Services.Entities;
using System;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IDeltaCalculatorService
    {
        public Guid RunId { get; set; }
        Task<DeltaResponse> CalculateDifferenceAsync(GroupMembership sourceMembership, GroupMembership destinationMembership);
    }
}
