// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IGraphUserRepository
    {
        public Task<IList<GraphProfileInformation>> GetAzureADObjectIdsAsync(IList<string> personnelNumbers, Guid? runId);
    }
}