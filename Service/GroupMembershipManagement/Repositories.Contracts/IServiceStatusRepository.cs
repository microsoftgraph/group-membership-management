// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IServiceStatusRepository
    {
        Task<ServiceStatuses> GetCurrentServiceStatusAsync();
        Task SetServiceStatusAsync(ServiceStatuses status, Guid requestorId);
    }
}
