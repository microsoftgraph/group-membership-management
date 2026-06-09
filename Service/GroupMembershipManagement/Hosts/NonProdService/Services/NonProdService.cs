// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.NonProdService;
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Services
{
    public class NonProdService : INonProdService
    {
        private readonly ILogger<NonProdService> _logger;
        private readonly IGraphGroupRepository _graphGroupRepository;

        public NonProdService(
            ILogger<NonProdService> logger,
            IGraphGroupRepository graphGroupRepository)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
        }

        public MembershipDifference GetMembershipDifference(List<AzureADUser> currentMembership, List<AzureADUser> targetMembership)
        {
            if(currentMembership == null || targetMembership == null)
            {
                throw new ArgumentNullException("Either the current or target membership input was null. GetMembershipDifference inputs must be lists");
            }

            var usersToAdd = targetMembership.Where(x => !currentMembership.Contains(x)).ToList();
            var usersToRemove = currentMembership.Where(x => !targetMembership.Contains(x)).ToList();

            return new MembershipDifference
            {
                UsersToAdd = usersToAdd,
                UsersToRemove = usersToRemove
            };
        }

        public async Task<GroupOwnershipResult> EnsureGroupOwnershipAsync(List<Guid> managedGroupIds, Guid ownerAppId, Guid runId)
        {
            _graphGroupRepository.RunId = runId;

            var result = new GroupOwnershipResult
            {
                TotalManagedGroups = managedGroupIds.Count,
                FailedGroupIds = new List<Guid>()
            };

            var ownerObjectId = await _graphGroupRepository.GetObjectIdFromAppIdAsync(ownerAppId, runId);
            if (ownerObjectId == Guid.Empty)
            {
                throw new InvalidOperationException($"Failed to resolve owner AppId {ownerAppId} to an ObjectId.");
            }

            _logger.ResolvedOwnerAppId(ownerAppId, ownerObjectId);

            var ownedGroupIds = await _graphGroupRepository.GetGroupIdsOwnedByServicePrincipalAsync(ownerObjectId);
            var ownedSet = new HashSet<Guid>(ownedGroupIds);

            var unownedGroupIds = managedGroupIds.Where(id => !ownedSet.Contains(id)).ToList();
            result.GroupsAlreadyOwned = managedGroupIds.Count - unownedGroupIds.Count;

            _logger.OwnershipDiff(managedGroupIds.Count, result.GroupsAlreadyOwned, unownedGroupIds.Count);

            foreach (var groupId in unownedGroupIds)
            {
                try
                {
                    await _graphGroupRepository.AddGroupOwners(groupId.ToString(), new List<Guid> { ownerObjectId });
                    result.GroupsNewlyOwned++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.GroupsFailed++;
                    result.FailedGroupIds.Add(groupId);
                    _logger.FailedToAddOwner(groupId, ex.Message);
                }

                var processed = result.GroupsNewlyOwned + result.GroupsFailed;
                if (processed % 500 == 0)
                {
                    _logger.OwnershipProgress(processed, unownedGroupIds.Count, result.GroupsFailed);
                }
            }

            return result;
        }
    }
}
