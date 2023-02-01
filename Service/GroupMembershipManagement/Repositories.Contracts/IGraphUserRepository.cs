// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.Entities;
using Microsoft.Graph;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IGraphUserRepository
    {
        public Task<IList<GraphProfileInformation>> GetAzureADObjectIdsAsync(IList<string> personnelNumbers, Guid? runId);

        public Task<List<GraphProfileInformation>> AddUsersAsync(List<User> users, Guid? runId);
    }
}