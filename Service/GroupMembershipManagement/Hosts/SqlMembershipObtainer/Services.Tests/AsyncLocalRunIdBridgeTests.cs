// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Services.Tests
{
    [TestClass]
    public class AsyncLocalRunIdBridgeTests
    {
        [TestMethod]
        public void ResolveRunId_ReturnsNull_WhenNoScopeActive()
        {
            var result = CorrelationActivity.ResolveRunId();
            Assert.IsNull(result);
        }

        [TestMethod]
        public void ResolveRunId_FindsRunId_WithinBeginSyncJobScope()
        {
            var expectedRunId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = expectedRunId
            };

            var logger = NullLogger.Instance;
            using (logger.BeginSyncJobScope(syncJob))
            {
                var resolved = CorrelationActivity.ResolveRunId();
                Assert.IsNotNull(resolved);
                Assert.AreEqual(expectedRunId, resolved.Value);
            }
        }

        [TestMethod]
        public void ResolveRunId_ReturnsNull_AfterScopeDisposed()
        {
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid()
            };

            var logger = NullLogger.Instance;
            var scope = logger.BeginSyncJobScope(syncJob);
            scope?.Dispose();

            var resolved = CorrelationActivity.ResolveRunId();
            Assert.IsNull(resolved);
        }

        [TestMethod]
        public void ResolveRunId_RestoresPreviousValue_WithNestedScopes()
        {
            var outerRunId = Guid.NewGuid();
            var innerRunId = Guid.NewGuid();
            var outerJob = new SyncJob { Id = Guid.NewGuid(), RunId = outerRunId };
            var innerJob = new SyncJob { Id = Guid.NewGuid(), RunId = innerRunId };

            var logger = NullLogger.Instance;
            using (logger.BeginSyncJobScope(outerJob))
            {
                Assert.AreEqual(outerRunId, CorrelationActivity.ResolveRunId());

                using (logger.BeginSyncJobScope(innerJob))
                {
                    Assert.AreEqual(innerRunId, CorrelationActivity.ResolveRunId());
                }

                Assert.AreEqual(outerRunId, CorrelationActivity.ResolveRunId());
            }

            Assert.IsNull(CorrelationActivity.ResolveRunId());
        }

        [TestMethod]
        public void ResolveRunId_WorksWithAdditionalProperties()
        {
            var expectedRunId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = expectedRunId
            };

            var logger = NullLogger.Instance;
            using (logger.BeginSyncJobScope(syncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = 1,
                ["TotalParts"] = 3
            }))
            {
                var resolved = CorrelationActivity.ResolveRunId();
                Assert.IsNotNull(resolved);
                Assert.AreEqual(expectedRunId, resolved.Value);
            }
        }

        [TestMethod]
        public async Task ResolveRunId_FlowsAcrossAwaitBoundaries()
        {
            var expectedRunId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = expectedRunId
            };

            var logger = NullLogger.Instance;
            using (logger.BeginSyncJobScope(syncJob))
            {
                await Task.Delay(10);
                var resolved = CorrelationActivity.ResolveRunId();
                Assert.IsNotNull(resolved);
                Assert.AreEqual(expectedRunId, resolved.Value);

                await Task.Yield();
                resolved = CorrelationActivity.ResolveRunId();
                Assert.AreEqual(expectedRunId, resolved.Value);
            }
        }

        [TestMethod]
        public void ResolveRunId_ExplicitParam_TakesPriorityOverAsyncLocal()
        {
            var asyncLocalRunId = Guid.NewGuid();
            var explicitRunId = Guid.NewGuid();
            var syncJob = new SyncJob { Id = Guid.NewGuid(), RunId = asyncLocalRunId };

            var logger = NullLogger.Instance;
            using (logger.BeginSyncJobScope(syncJob))
            {
                var resolved = CorrelationActivity.ResolveRunId(explicitRunId);
                Assert.AreEqual(explicitRunId, resolved.Value);
            }
        }

        [TestMethod]
        public void SetScopedRunId_ReturnsNull_ForEmptyGuid()
        {
            var scope = CorrelationActivity.SetScopedRunId(Guid.Empty);
            Assert.IsNull(scope);

            scope = CorrelationActivity.SetScopedRunId(null);
            Assert.IsNull(scope);
        }
    }
}