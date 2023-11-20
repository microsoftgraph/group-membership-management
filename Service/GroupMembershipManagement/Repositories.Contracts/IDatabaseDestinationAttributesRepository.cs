// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseDestinationAttributesRepository
    {
        Task UpdateAttributes(DestinationAttributes destinationAttributes);
    }
}
