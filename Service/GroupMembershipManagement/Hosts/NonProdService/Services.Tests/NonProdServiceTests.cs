// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class NonProdServiceTests
    {
        private Mock<IGraphGroupRepository> _mockGraphGroupRepository;
        private NonProdService _service;

        private readonly Guid _ownerAppId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private readonly Guid _ownerObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private readonly Guid _runId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        [TestInitialize]
        public void InitializeTest()
        {
            _mockGraphGroupRepository = new Mock<IGraphGroupRepository>();

            _service = new NonProdService(
                NullLogger<NonProdService>.Instance,
                _mockGraphGroupRepository.Object);
        }

        [TestMethod]
        public void GetNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                _service.GetMembershipDifference(null, null)
            );
        }

        [TestMethod]
        public void GetZeroDiff()
        {
            var membership = new List<AzureADUser>()
            {
                new AzureADUser() { ObjectId = Guid.NewGuid() },
                new AzureADUser() { ObjectId = Guid.NewGuid() },
                new AzureADUser() { ObjectId = Guid.NewGuid() }
            };

            var zeroDiff = _service.GetMembershipDifference(membership, membership);

            Assert.AreEqual(zeroDiff.UsersToAdd.Count, 0);
            Assert.AreEqual(zeroDiff.UsersToRemove.Count, 0);
        }

        [TestMethod]
        public void GetNonzeroDiff()
        {
            var currentMembership = new List<AzureADUser>()
            {
                new AzureADUser() { ObjectId = Guid.NewGuid() },
                new AzureADUser() { ObjectId = Guid.NewGuid() },
                new AzureADUser() { ObjectId = Guid.NewGuid() }
            };

            var targetMembership = new List<AzureADUser>()
            {
                new AzureADUser() { ObjectId = Guid.NewGuid() }
            };

            var membershipDiff = _service.GetMembershipDifference(currentMembership, targetMembership);

            Assert.AreEqual(membershipDiff.UsersToAdd.Count, 1);
            Assert.AreEqual(membershipDiff.UsersToRemove.Count, 3);
        }

        [TestMethod]
        public async Task EnsureGroupOwnershipAsync_HappyPath_AddsOwnersToUnownedGroups()
        {
            var managedGroupIds = new List<Guid>
            {
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee")
            };

            var alreadyOwnedGroupIds = new List<Guid>
            {
                managedGroupIds[0],
                managedGroupIds[2],
                managedGroupIds[4]
            };

            _mockGraphGroupRepository
                .Setup(x => x.GetObjectIdFromAppIdAsync(_ownerAppId, _runId))
                .ReturnsAsync(_ownerObjectId);

            _mockGraphGroupRepository
                .Setup(x => x.GetGroupIdsOwnedByServicePrincipalAsync(_ownerObjectId))
                .ReturnsAsync(alreadyOwnedGroupIds);

            _mockGraphGroupRepository
                .Setup(x => x.AddGroupOwners(It.IsAny<string>(), It.IsAny<List<Guid>>()))
                .Returns(Task.CompletedTask);

            var result = await _service.EnsureGroupOwnershipAsync(managedGroupIds, _ownerAppId, _runId);

            Assert.AreEqual(5, result.TotalManagedGroups);
            Assert.AreEqual(3, result.GroupsAlreadyOwned);
            Assert.AreEqual(2, result.GroupsNewlyOwned);
            Assert.AreEqual(0, result.GroupsFailed);
            Assert.AreEqual(0, result.FailedGroupIds.Count);

            _mockGraphGroupRepository.Verify(
                x => x.AddGroupOwners(managedGroupIds[1].ToString(), It.Is<List<Guid>>(l => l.Contains(_ownerObjectId))),
                Times.Once);
            _mockGraphGroupRepository.Verify(
                x => x.AddGroupOwners(managedGroupIds[3].ToString(), It.Is<List<Guid>>(l => l.Contains(_ownerObjectId))),
                Times.Once);
        }

        [TestMethod]
        public async Task EnsureGroupOwnershipAsync_AllAlreadyOwned_NoAdditions()
        {
            var managedGroupIds = new List<Guid>
            {
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb")
            };

            _mockGraphGroupRepository
                .Setup(x => x.GetObjectIdFromAppIdAsync(_ownerAppId, _runId))
                .ReturnsAsync(_ownerObjectId);

            _mockGraphGroupRepository
                .Setup(x => x.GetGroupIdsOwnedByServicePrincipalAsync(_ownerObjectId))
                .ReturnsAsync(new List<Guid>(managedGroupIds));

            var result = await _service.EnsureGroupOwnershipAsync(managedGroupIds, _ownerAppId, _runId);

            Assert.AreEqual(2, result.TotalManagedGroups);
            Assert.AreEqual(2, result.GroupsAlreadyOwned);
            Assert.AreEqual(0, result.GroupsNewlyOwned);
            Assert.AreEqual(0, result.GroupsFailed);

            _mockGraphGroupRepository.Verify(
                x => x.AddGroupOwners(It.IsAny<string>(), It.IsAny<List<Guid>>()),
                Times.Never);
        }

        [TestMethod]
        public async Task EnsureGroupOwnershipAsync_PerGroupErrorIsolation_ContinuesOnFailure()
        {
            var managedGroupIds = new List<Guid>
            {
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc")
            };

            _mockGraphGroupRepository
                .Setup(x => x.GetObjectIdFromAppIdAsync(_ownerAppId, _runId))
                .ReturnsAsync(_ownerObjectId);

            _mockGraphGroupRepository
                .Setup(x => x.GetGroupIdsOwnedByServicePrincipalAsync(_ownerObjectId))
                .ReturnsAsync(new List<Guid>());

            _mockGraphGroupRepository
                .Setup(x => x.AddGroupOwners(managedGroupIds[0].ToString(), It.IsAny<List<Guid>>()))
                .Returns(Task.CompletedTask);
            _mockGraphGroupRepository
                .Setup(x => x.AddGroupOwners(managedGroupIds[1].ToString(), It.IsAny<List<Guid>>()))
                .ThrowsAsync(new Exception("Graph API error"));
            _mockGraphGroupRepository
                .Setup(x => x.AddGroupOwners(managedGroupIds[2].ToString(), It.IsAny<List<Guid>>()))
                .Returns(Task.CompletedTask);

            var result = await _service.EnsureGroupOwnershipAsync(managedGroupIds, _ownerAppId, _runId);

            Assert.AreEqual(3, result.TotalManagedGroups);
            Assert.AreEqual(0, result.GroupsAlreadyOwned);
            Assert.AreEqual(2, result.GroupsNewlyOwned);
            Assert.AreEqual(1, result.GroupsFailed);
            Assert.AreEqual(1, result.FailedGroupIds.Count);
            Assert.AreEqual(managedGroupIds[1], result.FailedGroupIds[0]);
        }

        [TestMethod]
        public async Task EnsureGroupOwnershipAsync_EmptyManagedList_ReturnsZeros()
        {
            var managedGroupIds = new List<Guid>();

            _mockGraphGroupRepository
                .Setup(x => x.GetObjectIdFromAppIdAsync(_ownerAppId, _runId))
                .ReturnsAsync(_ownerObjectId);

            _mockGraphGroupRepository
                .Setup(x => x.GetGroupIdsOwnedByServicePrincipalAsync(_ownerObjectId))
                .ReturnsAsync(new List<Guid>());

            var result = await _service.EnsureGroupOwnershipAsync(managedGroupIds, _ownerAppId, _runId);

            Assert.AreEqual(0, result.TotalManagedGroups);
            Assert.AreEqual(0, result.GroupsAlreadyOwned);
            Assert.AreEqual(0, result.GroupsNewlyOwned);
            Assert.AreEqual(0, result.GroupsFailed);

            _mockGraphGroupRepository.Verify(
                x => x.AddGroupOwners(It.IsAny<string>(), It.IsAny<List<Guid>>()),
                Times.Never);
        }

        [TestMethod]
        public async Task EnsureGroupOwnershipAsync_AppIdResolutionFailure_ThrowsInvalidOperationException()
        {
            _mockGraphGroupRepository
                .Setup(x => x.GetObjectIdFromAppIdAsync(_ownerAppId, _runId))
                .ReturnsAsync(Guid.Empty);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => _service.EnsureGroupOwnershipAsync(
                    new List<Guid> { Guid.NewGuid() }, _ownerAppId, _runId));
        }

        [TestMethod]
        public async Task EnsureGroupOwnershipAsync_RunIdPropagation_SetsRunIdOnRepository()
        {
            _mockGraphGroupRepository
                .Setup(x => x.GetObjectIdFromAppIdAsync(_ownerAppId, _runId))
                .ReturnsAsync(_ownerObjectId);

            _mockGraphGroupRepository
                .Setup(x => x.GetGroupIdsOwnedByServicePrincipalAsync(_ownerObjectId))
                .ReturnsAsync(new List<Guid>());

            await _service.EnsureGroupOwnershipAsync(new List<Guid>(), _ownerAppId, _runId);

            _mockGraphGroupRepository.VerifySet(x => x.RunId = _runId, Times.Once);
        }
    }
}
