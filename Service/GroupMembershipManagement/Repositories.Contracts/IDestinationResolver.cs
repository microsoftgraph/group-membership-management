// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts.DestinationResolution;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDestinationResolver
    {
        Task<ResolvedDestination> ResolveAsync(SyncJob syncJob, CancellationToken cancellationToken = default);
    }
}
