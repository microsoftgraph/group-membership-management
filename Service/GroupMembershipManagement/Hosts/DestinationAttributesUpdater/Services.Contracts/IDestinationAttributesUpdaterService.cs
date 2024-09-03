// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace Services.Contracts
{
    public interface IDestinationAttributesUpdaterService
    {
        Task<List<DestinationReaderResponse>> GetDestinationsAsync(string destinationType);
        Task<List<DestinationAttributes>> GetBulkDestinationAttributesAsync(List<DestinationReaderResponse> destinations, string destinationType);
        Task UpdateAttributes(DestinationAttributes destinationAttributes);
    }
}
