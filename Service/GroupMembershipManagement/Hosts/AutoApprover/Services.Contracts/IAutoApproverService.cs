// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ServiceBus;
using System.Threading.Tasks;

namespace Services.AutoApprover.Contracts
{
    public interface IAutoApproverService
    {
        Task ProcessAutoApprovalAsync(AutoApprovalQueueMessage message);
    }
}
