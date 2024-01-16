// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Services.Contracts
{
    public interface IDestinationAttributesUpdaterService
    {
        Task<List<(string Destination, Guid JobId)>> GetDestinationsAsync(string destinationType);
        Task<List<DestinationAttributes>> GetBulkDestinationAttributesAsync(List<(string Destination, Guid JobId)> destinations, string destinationType);
        Task UpdateAttributes(DestinationAttributes destinationAttributes);
    }
}
