// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Services.Contracts
{
    public interface IDestinationAttributesUpdaterService
    {
        Task<List<DestinationInfo>> GetDestinationsAsync(string destinationType);
        Task<List<DestinationAttributes>> GetBulkDestinationAttributesAsync(List<DestinationInfo> destinations, string destinationType);
        Task UpdateAttributes(DestinationAttributes destinationAttributes);
    }
}
