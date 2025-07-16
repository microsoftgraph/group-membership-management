// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.ServiceBus;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface ISyncJobUpdaterService
    {
        Task UpdateSyncJobStatusAsync(JobStatusUpdateQueueMessage message);
    }
}
