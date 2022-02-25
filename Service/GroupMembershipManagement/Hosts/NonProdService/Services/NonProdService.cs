// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services
{
    public class NonProdService : INonProdService
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly string _gmmAppId;

        private enum GroupEnums
        {
            TestGroup1Member,
            TestGroup10Members,
            TestGroup100Members,
            TestGroup1kMembers,
            TestGroup10kMembers
        }

        private Hashtable _groupSizes = new Hashtable()
        {
            { GroupEnums.TestGroup1Member, 1 },
            { GroupEnums.TestGroup10Members, 10 },
            { GroupEnums.TestGroup100Members, 100 },
            { GroupEnums.TestGroup1kMembers, 1000},
            { GroupEnums.TestGroup10kMembers, 10000 }
        };

        public NonProdService(
            ILoggingRepository loggingRepository,
            IGraphGroupRepository graphGroupRepository,
            IKeyVaultSecret<INonProdService> gmmAppId
            )
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _gmmAppId = gmmAppId.Secret;
        }

        public async Task<bool> CreateTestGroups()
        {
            foreach (var groupName in _groupSizes.Keys)
            {
                await _graphGroupRepository.CreateGroup(groupName.ToString());
            }

            return true;
        }

        public async Task<bool> FillTestGroups()
        {
            var tenantUsersRequired = (int) _groupSizes[GroupEnums.TestGroup10kMembers];
            var tenantUsers = await _graphGroupRepository.GetTenantUsers(tenantUsersRequired);

            foreach (var groupName in _groupSizes.Keys)
            {
                var groupUserCount = (int)_groupSizes[groupName];
                var desiredMembership = tenantUsers.Take(groupUserCount);
                var group = await _graphGroupRepository.GetGroup(groupName.ToString());

                var usersInGroup = await _graphGroupRepository.GetUsersInGroupTransitively(group.ObjectId);

                var usersToAdd = desiredMembership.Where(x => !usersInGroup.Contains(x)).ToList();
                var usersToRemove = usersInGroup.Where(x => !desiredMembership.Contains(x)).ToList();

                if (group == null)
                    return false;

                if(usersToAdd.Count > 0)
                    await _graphGroupRepository.AddUsersToGroup(usersToAdd, group);
        
                if(usersToRemove.Count > 0)
                    await _graphGroupRepository.RemoveUsersFromGroup(usersToRemove, group);
            }

            return true;
        }
    }
}
