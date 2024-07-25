// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using WebApi.Models;

namespace Services.WebApi.Contracts
{
    public interface IOperationsTaskQueue
    {
        Task QueueAsync(OperationDetails operation);
        Task<OperationDetails?> DequeueAsync();
    }
}
