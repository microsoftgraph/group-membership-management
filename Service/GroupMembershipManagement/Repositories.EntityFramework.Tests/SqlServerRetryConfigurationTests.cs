// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repositories.EntityFramework.Contexts;

namespace Repositories.EntityFramework.Tests
{
    [TestClass]
    public class SqlServerRetryConfigurationTests
    {
        [TestMethod]
        public void EnableGmmRetryOnFailure_PreservesConnectionTimeoutAndRetriesError258()
        {
            var options = new DbContextOptionsBuilder<GMMContext>()
                .UseSqlServer(
                    "Server=(unused);Database=retry-configuration;Trusted_Connection=True;Connection Timeout=90;",
                    sqlServerOptions => sqlServerOptions.EnableGmmRetryOnFailure())
                .Options;

            using var context = new GMMContext(options);

            var retryStrategy = (SqlServerRetryingExecutionStrategy)context.Database.CreateExecutionStrategy();

            Assert.AreEqual(90, context.Database.GetDbConnection().ConnectionTimeout);
            Assert.AreEqual(1, retryStrategy.MaxRetryCount);
            Assert.AreEqual(TimeSpan.FromSeconds(10), retryStrategy.MaxRetryDelay);
            CollectionAssert.Contains(retryStrategy.AdditionalErrorNumbers!.ToArray(), 258);
        }

        [TestMethod]
        public void EnableGmmRetryOnFailure_WithNullBuilder_ThrowsArgumentNullException()
        {
            SqlServerDbContextOptionsBuilder? optionsBuilder = null;

            Assert.ThrowsException<ArgumentNullException>(
                () => optionsBuilder!.EnableGmmRetryOnFailure());
        }
    }
}
