// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models.ServiceBus;
using Services.Entities;
using System;
using System.Threading.Tasks;
using Models;

namespace Services.Contracts
{
    public interface IDeltaCalculatorService
    {
        public Guid RunId { get; set; }
        Task<Guid> GetGroupIdAsync(SyncJob syncJob);
        Task<string> GetChannelIdAsync(SyncJob syncJob);
        Task<DeltaResponse> CalculateDifferenceAsync(GroupMembership sourceMembership, GroupMembership destinationMembership);
    }
}
