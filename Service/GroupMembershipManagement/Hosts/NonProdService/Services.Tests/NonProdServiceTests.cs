// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repositories.Mocks;
using Services.Contracts;
using System;
using System.Collections.Generic;

namespace Services.Tests
{
    [TestClass]
    public class NonProdServiceTests
    {
        INonProdService _nonProdService = null;

        [TestInitialize]
        public void InitializeTest()
        {
            var loggingRepository = new MockLoggingRepository();
            _nonProdService = new NonProdService(loggingRepository);
        }

        [TestMethod]
        public void GetNullException()
        {
            Assert.ThrowsException<ArgumentNullException>(() =>
                _nonProdService.GetMembershipDifference(null, null)
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

            var zeroDiff = _nonProdService.GetMembershipDifference(membership, membership);

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

            var membershipDiff = _nonProdService.GetMembershipDifference(currentMembership, targetMembership);

            Assert.AreEqual(membershipDiff.UsersToAdd.Count, 1);
            Assert.AreEqual(membershipDiff.UsersToRemove.Count, 3);

        }
    }
}
