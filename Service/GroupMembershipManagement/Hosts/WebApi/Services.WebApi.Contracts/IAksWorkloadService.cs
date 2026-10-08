// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Services.Contracts
{
    public interface IAksWorkloadService
    {
        Task StopAllWorkloadsAsync(CancellationToken cancellationToken);
        Task StartWorkloadAsync(string deploymentName, CancellationToken cancellationToken);
    }
}
