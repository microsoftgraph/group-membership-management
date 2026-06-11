// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockDatabaseGroupsRepository : IDatabaseGroupsRepository
    {
        public List<Group> Groups { get; set; } = new List<Group>();


        public async Task<Group> GetGroupAsync(Guid groupId)
        {
            var group = Groups.FirstOrDefault(x => x.GroupId == groupId);
            return await Task.FromResult(group);
        }

        public async Task<Group> GetGroupUsingSyncJobIdAsync(Guid syncJobId)
        {
            var group = Groups.FirstOrDefault(x => x.SyncJobId == syncJobId);
            return await Task.FromResult(group);
        }
    }
}