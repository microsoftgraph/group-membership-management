// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.Contracts
{
    public interface IResourceManagerService
    {
        Task StartWebSitesAsync(Guid requestorId, CancellationToken cancellationToken);
        Task StopWebSitesAsync(Guid requestorId, CancellationToken cancellationToken);
        Task ResetWebSitesAsync(Guid requestorId, CancellationToken cancellationToken);
    }
}
