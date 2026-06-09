// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.Contracts
{
    public interface IResourceManagerService
    {
        Task StartWebSitesAsync(Guid requestorId, CancellationToken cancellationToken);
        Task StartWebSiteAsync(Guid requestorId, string websiteName, CancellationToken cancellationToken);
        Task StopWebSitesAsync(Guid requestorId, CancellationToken cancellationToken);
    }
}
