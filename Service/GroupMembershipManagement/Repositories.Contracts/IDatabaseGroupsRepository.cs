// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseGroupsRepository
    {       
        Task<Group> GetGroupAsync(Guid groupId);
    }
}