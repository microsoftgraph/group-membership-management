// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.MessageSplitter;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Services.Tests
{
    [TestClass]
    public class RunLimiterTests
    {
        [TestMethod]
        public void Acquire_AllowsFirstLease()
        {
            var entity = new RunLimiter();
            var now = DateTimeOffset.UtcNow;

            var resp = entity.Acquire(new AcquireLeaseRequest(Guid.NewGuid(), MaxInFlight: 1, LeaseTimeoutMinutes: 10, UtcNow: now));

            Assert.IsTrue(resp.Acquired);
            Assert.AreEqual(1, resp.InFlightCount);
            Assert.IsNotNull(resp.LeaseExpiresAt);
        }

        [TestMethod]
        public void Acquire_DeniesWhenAtCapacity()
        {
            var entity = new RunLimiter();
            var now = DateTimeOffset.UtcNow;

            entity.Acquire(new AcquireLeaseRequest(Guid.NewGuid(), MaxInFlight: 1, LeaseTimeoutMinutes: 10, UtcNow: now));
            var resp2 = entity.Acquire(new AcquireLeaseRequest(Guid.NewGuid(), MaxInFlight: 1, LeaseTimeoutMinutes: 10, UtcNow: now));

            Assert.IsFalse(resp2.Acquired);
            Assert.AreEqual(1, resp2.InFlightCount);
            Assert.IsNull(resp2.LeaseExpiresAt);
        }

        [TestMethod]
        public void Acquire_PrunesExpiredLeases()
        {
            var entity = new RunLimiter();
            var run1 = Guid.NewGuid();
            var run2 = Guid.NewGuid();

            var t0 = DateTimeOffset.UtcNow;
            entity.Acquire(new AcquireLeaseRequest(run1, MaxInFlight: 1, LeaseTimeoutMinutes: 1, UtcNow: t0));

            var t1 = t0.AddMinutes(2);
            var resp = entity.Acquire(new AcquireLeaseRequest(run2, MaxInFlight: 1, LeaseTimeoutMinutes: 10, UtcNow: t1));

            Assert.IsTrue(resp.Acquired);
            Assert.AreEqual(1, resp.InFlightCount);
        }

        [TestMethod]
        public void Release_ReturnsTrue_WhenLeaseExists()
        {
            var entity = new RunLimiter();
            var runId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            entity.Acquire(new AcquireLeaseRequest(runId, MaxInFlight: 1, LeaseTimeoutMinutes: 10, UtcNow: now));

            Assert.IsTrue(entity.Release(runId));
        }
    }
}
