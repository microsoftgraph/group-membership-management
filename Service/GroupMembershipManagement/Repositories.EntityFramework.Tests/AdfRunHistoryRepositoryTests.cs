// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models.AdfRunHistory;
using System;
using System.IO;

namespace Repositories.EntityFramework.Tests
{
    [TestClass]
    public class AdfRunHistoryRepositoryTests
    {
        [TestMethod]
        public void Repository_UsesDeterministicTableAwareQuery()
        {
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<Repositories.EntityFramework.AdfContexts.AdfContext>()
                .UseSqlServer("Server=localhost;Database=none")
                .Options;
            using var context = new Repositories.EntityFramework.AdfContexts.AdfContext(options);

            var sql = context.AdfRunHistory.AsNoTracking()
                .Where(run => Repositories.EntityFramework.AdfContexts.AdfContext.ObjectId("users." + run.AdfRunId.ToString().Replace("-", ""), "U") != null)
                .OrderByDescending(run => run.PublishedAtUtc)
                .Take(1)
                .ToQueryString();

            StringAssert.Contains(sql, "[dbo].[AdfRunHistory]");
            StringAssert.Contains(sql, "OBJECT_ID(");
            StringAssert.Contains(sql, "REPLACE(");
            StringAssert.Contains(sql, "ORDER BY [a].[DateCreated] DESC");

            var source = ReadPublicFile(
                "Service/GroupMembershipManagement/Repositories.EntityFramework/AdfRunHistoryRepository.cs");
            StringAssert.Contains(source, "AdfContext.ObjectId(");
            Assert.IsFalse(source.Contains("FromSqlRaw"));
        }

        [TestMethod]
        public void Repository_ReturnsNullWhenNoEligibleRun()
        {
            var source = ReadPublicFile(
                "Service/GroupMembershipManagement/Repositories.EntityFramework/AdfRunHistoryRepository.cs");

            StringAssert.Contains(source, "Task<AdfRunHistory?> GetLatestAdfRunAsync");
            StringAssert.Contains(source, "return null;");
        }

        [TestMethod]
        public void Repository_LogsDistinctWarningsForEmptyHistoryAndMissingSnapshots()
        {
            var source = ReadPublicFile(
                "Service/GroupMembershipManagement/Repositories.EntityFramework/AdfRunHistoryRepository.cs");

            StringAssert.Contains(source, "contains no rows");
            StringAssert.Contains(source, "none has a published snapshot table");
            StringAssert.Contains(source, "{RecordedRunCount}");
            StringAssert.Contains(source, "{LatestAdfRunId}");
        }

        [TestMethod]
        public void PipelineTemplate_RecordsRunOnlyAfterAllPublishingActivitiesSucceed()
        {
            var template = ReadPublicFile("Infrastructure/adf/pipeline/azureDataFactory.bicep");
            var start = template.IndexOf("name: 'Record ADF Run History'", StringComparison.Ordinal);

            Assert.IsTrue(start >= 0, "Pipeline must record the run in AdfRunHistory.");
            var activity = template.Substring(start);
            StringAssert.Contains(activity, "INSERT INTO [dbo].[AdfRunHistory]");
            StringAssert.Contains(activity, "IF NOT EXISTS");
            foreach (var dependency in new[]
            {
                "Calculate Height",
                "PopulateMappingsTableDataFlow",
                "Create Agents Index",
                "Gate AgentReader Terminal Status",
            })
            {
                StringAssert.Contains(activity, $"activity: '{dependency}'");
            }
        }
        [TestMethod]
        public void PipelineTemplate_PrunesRunHistoryByAgeThenByMinimumRowCount()
        {
            var template = ReadPublicFile("Infrastructure/adf/pipeline/azureDataFactory.bicep");

            StringAssert.Contains(template, "param runHistoryRetentionDays int = 30");
            StringAssert.Contains(template, "param runHistoryMinimumRowsToRetain int = 10");
            StringAssert.Contains(template, "name: 'Clean Up Old Run History'");
            StringAssert.Contains(template, "WHERE [DateCreated] >= @Cutoff");
            StringAssert.Contains(template, "DELETE FROM [dbo].[AdfRunHistory] WHERE [DateCreated] < @Cutoff");
            StringAssert.Contains(template, "SELECT TOP (${runHistoryMinimumRowsToRetain}) [AdfRunId]");
        }
        [TestMethod]
        public void Repository_UsesAdfContext()
        {
            var source = ReadPublicFile(
                "Service/GroupMembershipManagement/Repositories.EntityFramework/AdfRunHistoryRepository.cs");

            // The repository depends on AdfContext directly.
            StringAssert.Contains(source, "AdfContext _context");
        }

        [TestMethod]
        public void Repository_ConfiguresSqlRetryWhereTheContextIsRegistered()
        {
            // Transient-fault retry now lives with the EF registration (EnableRetryOnFailure)
            // instead of inside the repository.
            var webApiStartup = ReadPublicFile(
                "Service/GroupMembershipManagement/Hosts/WebApi/WebApi/Program.cs");

            StringAssert.Contains(webApiStartup, "AddDbContext<AdfContext>");
            StringAssert.Contains(webApiStartup, "EnableRetryOnFailure");
        }

        [TestMethod]
        public void AdfContext_MapsAdfRunToAdfRunHistoryAndHasMigration()
        {
            var adfContext = ReadPublicFile(
                "Service/GroupMembershipManagement/Repositories.EntityFramework.AdfContexts/AdfContext.cs");
            var gmmContext = ReadPublicFile(
                "Service/GroupMembershipManagement/Repositories.EntityFramework.Contexts/GMMContext.cs");
            var migrations = Path.Combine(
                FindPublicRoot(),
                "Service",
                "GroupMembershipManagement",
                "Repositories.EntityFramework.AdfContexts",
                "Migrations");

            // AdfRunHistory belongs to AdfContext, not GMMContext.
            StringAssert.Contains(adfContext, "AdfRunId");
            StringAssert.Contains(adfContext, "AdfRunHistory");
            Assert.IsFalse(
                gmmContext.Contains("AdfRunHistory", StringComparison.Ordinal),
                "GMMContext must not map AdfRunHistory.");
            Assert.IsTrue(
                Directory.Exists(migrations)
                && Array.Exists(
                    Directory.GetFiles(migrations, "*.cs"),
                    file => File.ReadAllText(file).Contains("AdfRunHistory", StringComparison.Ordinal)),
                "Expected an EF migration for dbo.AdfRunHistory in the AdfContexts project.");
        }

        [TestMethod]
        public void AdfRunHistory_DerivesTableNameFromAdfRunId()
        {
            var adfRun = new AdfRunHistory
            {
                AdfRunId = Guid.Parse("2eb36329-a263-4704-869f-cf4f33d1de52")
            };

            Assert.AreEqual("2eb36329a2634704869fcf4f33d1de52", adfRun.GetTableName());
        }

        private static string ReadPublicFile(string relativePath)
        {
            var path = Path.Combine(
                FindPublicRoot(),
                relativePath.Replace('/', Path.DirectorySeparatorChar));

            Assert.IsTrue(File.Exists(path), $"Expected feature source file: {path}");
            return File.ReadAllText(path);
        }

        private static string FindPublicRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "Service", "GroupMembershipManagement"))
                    && Directory.Exists(Path.Combine(directory.FullName, "Infrastructure")))
                {
                    return directory.FullName;
                }
            }

            Assert.Fail("Could not locate the public repository root.");
            return string.Empty;
        }
    }
}
