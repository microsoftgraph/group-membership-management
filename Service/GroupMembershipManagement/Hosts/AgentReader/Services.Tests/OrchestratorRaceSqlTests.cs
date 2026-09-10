// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.AgentReader.Services;
using Hosts.AgentReader.Services.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.AgentsTable;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.AgentReader.Services.Tests
{
    /// <summary>
    /// Exercises the consequence of a duplicate orchestration start against real SQL.
    /// Durable's own restart semantics are not under test here - those were established separately
    /// against the pinned runtime. What these tests establish is what a second execution does to the
    /// table, which is the property FR-012/SC-007 actually protect.
    /// </summary>
    [TestClass]
    [TestCategory("LocalSqlIntegration")]
    public class OrchestratorRaceSqlTests
    {
        private const string SecondPageLink =
            "https://graph.microsoft.com/v1.0/users/microsoft.graph.agentUser?$skiptoken=page2";

        private static string _masterConnectionString = string.Empty;
        private static string _connectionString = string.Empty;
        private static string _databaseName = string.Empty;
        private static bool _databaseCreated;

        [ClassInitialize]
        public static async Task CreateFixtureDatabase(TestContext context)
        {
            var server = Environment.GetEnvironmentVariable("AGENTREADER_SQL_TEST_SERVER");
            if (string.IsNullOrEmpty(server))
            {
                Assert.Inconclusive("Set AGENTREADER_SQL_TEST_SERVER to an isolated (localdb)\\GmmAgentReader-<suffix> instance.");
            }
            Assert.IsTrue(Regex.IsMatch(server, @"^\(localdb\)\\GmmAgentReader-[a-zA-Z0-9-]+$", RegexOptions.IgnoreCase),
                "SQL fixture DDL is permitted only on an explicitly named, isolated local test instance.");

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = "master",
                IntegratedSecurity = true,
                Encrypt = SqlConnectionEncryptOption.Optional,
                Pooling = false
            };
            _masterConnectionString = builder.ConnectionString;
            _databaseName = $"AgentReaderRaceTests_{Guid.NewGuid():N}";
            await ExecuteAsync(_masterConnectionString, $"CREATE DATABASE [{_databaseName}];");
            _databaseCreated = true;
            builder.InitialCatalog = _databaseName;
            _connectionString = builder.ConnectionString;
            await ExecuteAsync(_connectionString, "CREATE SCHEMA agents;");
        }

        [ClassCleanup]
        public static async Task DropFixtureDatabase()
        {
            if (_databaseCreated)
            {
                await ExecuteAsync(_masterConnectionString, $"DROP DATABASE [{_databaseName}];");
            }
        }

        [TestMethod]
        public async Task SingleRunEstablishesTheBaseline()
        {
            var runId = Guid.NewGuid();
            await CreateTableAsync(runId);

            var result = await RunOrchestrationAsync(runId);

            Assert.AreEqual("Completed", result.Status);
            CollectionAssert.AreEqual(ExpectedRows(), await SnapshotAsync(runId));
        }

        [TestMethod]
        public async Task RestartAfterCompletionFailsWithoutChangingTheTable()
        {
            var runId = Guid.NewGuid();
            await CreateTableAsync(runId);
            await RunOrchestrationAsync(runId);
            var baseline = await SnapshotAsync(runId);

            // The reproduced race: both callers probed null, the first ran to completion, and the
            // second then scheduled the same instance id - which Durable restarts.
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => RunOrchestrationAsync(runId));

            CollectionAssert.AreEqual(baseline, await SnapshotAsync(runId));
        }

        [TestMethod]
        public async Task ConcurrentRunsNeverDuplicateRows()
        {
            var runId = Guid.NewGuid();
            await CreateTableAsync(runId);

            // Both runs are held at their first Graph page, which is after ValidateAgentTable. That
            // forces the interleaving worth proving safe - two executions that both saw an empty
            // table and then write concurrently - rather than leaving it to timing.
            var gate = new Gate(2);
            var runs = await Task.WhenAll(
                Enumerable.Range(0, 2).Select(_ => Task.Run(() => TryRunOrchestrationAsync(runId, gate))));

            var rows = await SnapshotAsync(runId);
            var expected = ExpectedRows();

            Assert.IsTrue(gate.Opened, "The runs did not overlap, so the concurrent path was never exercised.");
            CollectionAssert.AreEqual(new[] { "Completed", "Completed" }, runs);
            CollectionAssert.AllItemsAreUnique(rows.Select(row => row.Split('|')[0]).ToArray());
            CollectionAssert.AreEqual(expected, rows);
        }

        [TestMethod]
        public async Task RestartAfterAnEmptyRunStaysEmpty()
        {
            var runId = Guid.NewGuid();
            await CreateTableAsync(runId);

            // The empty-table precondition cannot catch this restart, because a run that persisted
            // nothing leaves the table in exactly the state a first run expects. It is safe only
            // because there is nothing to duplicate.
            Assert.AreEqual("CompletedEmpty", (await RunOrchestrationAsync(runId, ineligibleOnly: true)).Status);
            Assert.AreEqual("CompletedEmpty", (await RunOrchestrationAsync(runId, ineligibleOnly: true)).Status);

            Assert.AreEqual(0, (await SnapshotAsync(runId)).Count);
        }

        private static async Task<string> TryRunOrchestrationAsync(Guid runId, Gate? gate = null)
        {
            try
            {
                return (await RunOrchestrationAsync(runId, gate: gate)).Status;
            }
            catch (Exception exception)
            {
                return exception.GetType().Name;
            }
        }

        private static Task<AgentReaderResult> RunOrchestrationAsync(
            Guid runId, bool ineligibleOnly = false, Gate? gate = null)
        {
            var service = new AgentReaderService(
                new FakeGraphRepository(ineligibleOnly ? IneligiblePages() : SourcePages(), gate),
                new AgentTableRepository(
                    new SqlConnectionFactory(_connectionString), NullLogger<AgentTableRepository>.Instance),
                NullLogger<AgentReaderService>.Instance);
            var tableFunctions = new AgentTableFunctions(service, NullLogger<AgentTableFunctions>.Instance);
            var readerFunction = new AgentReaderFunction(service, NullLogger<AgentReaderFunction>.Instance);
            var writerFunction = new AgentWriterFunction(service, NullLogger<AgentWriterFunction>.Instance);

            var context = new Mock<TaskOrchestrationContext>(MockBehavior.Strict);
            context.Setup(item => item.GetInput<AgentReaderRequest>()).Returns(new AgentReaderRequest(runId));
            context.Setup(item => item.CreateReplaySafeLogger("Hosts.AgentReader.OrchestratorFunction"))
                .Returns(NullLogger.Instance);
            context.Setup(item => item.CallActivityAsync(
                    nameof(AgentTableFunctions.ValidateAgentTable), It.IsAny<AgentReaderRequest>(), It.IsAny<TaskOptions>()))
                .Returns((TaskName name, object input, TaskOptions? options) =>
                    tableFunctions.ValidateAgentTable((AgentReaderRequest)input, CancellationToken.None));
            context.Setup(item => item.CallActivityAsync<AgentPage>(
                    nameof(AgentReaderFunction), It.IsAny<AgentPageRequest>(), It.IsAny<TaskOptions>()))
                .Returns((TaskName name, object input, TaskOptions? options) =>
                    readerFunction.RunAsync((AgentPageRequest)input, CancellationToken.None));
            context.Setup(item => item.CallActivityAsync<int>(
                    nameof(AgentWriterFunction), It.IsAny<AgentWriteRequest>(), It.IsAny<TaskOptions>()))
                .Returns((TaskName name, object input, TaskOptions? options) =>
                    writerFunction.RunAsync((AgentWriteRequest)input, CancellationToken.None));
            context.Setup(item => item.CallActivityAsync<long>(
                    nameof(AgentTableFunctions.GetAgentRowCount), It.IsAny<AgentReaderRequest>(), It.IsAny<TaskOptions>()))
                .Returns((TaskName name, object input, TaskOptions? options) =>
                    tableFunctions.GetAgentRowCount((AgentReaderRequest)input, CancellationToken.None));

            return new OrchestratorFunction().RunAsync(context.Object);
        }

        private static Guid AgentId(int index) => new($"aaaaaaaa-0000-0000-0000-{index:D12}");

        private static AgentSourcePage[] SourcePages() =>
        [
            new([
                new AgentSourceRecord(AgentId(1).ToString("D"), true, "4815", "bp-alpha", true),
                new AgentSourceRecord(AgentId(2).ToString("D"), true, " 162342 ", null, false)
            ], SecondPageLink),
            new([
                new AgentSourceRecord(AgentId(3).ToString("D"), true, "9001", "bp-gamma", null),
                new AgentSourceRecord(AgentId(4).ToString("D"), false, null, null, true)
            ], null)
        ];

        private static AgentSourcePage[] IneligiblePages() =>
        [
            new([new AgentSourceRecord(AgentId(9).ToString("D"), false, null, null, true)], null)
        ];

        private static List<string> ExpectedRows() =>
        [
            $"{AgentId(1):D}|4815|bp-alpha|True",
            $"{AgentId(2):D}|162342|<null>|False",
            $"{AgentId(3):D}|9001|bp-gamma|True"
        ];

        private static async Task<List<string>> SnapshotAsync(Guid runId)
        {
            var rows = new List<string>();
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = $"""
                SELECT AgentObjectId, ManagerId, BlueprintId, AccountEnabled
                FROM [agents].[{runId:N}]
                ORDER BY AgentObjectId, ManagerId, AccountEnabled;
                """;
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add(string.Join('|',
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.IsDBNull(2) ? "<null>" : reader.GetString(2),
                    reader.GetBoolean(3)));
            }

            return rows;
        }

        private static async Task CreateTableAsync(Guid runId)
        {
            await ExecuteAsync(_connectionString, $"""
                CREATE TABLE [agents].[{runId:N}] (
                    AgentObjectId nvarchar(36) NOT NULL,
                    ManagerId int NOT NULL,
                    BlueprintId nvarchar(200) NULL,
                    AccountEnabled bit NOT NULL
                );
                """);
            await ExecuteAsync(_connectionString,
                $"CREATE CLUSTERED INDEX IDX_Agents_ManagerId ON [agents].[{runId:N}](ManagerId);");
        }

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        private sealed class SqlConnectionFactory(string connectionString) : IAgentSqlConnectionFactory
        {
            public DbConnection CreateConnection() => new SqlConnection(connectionString);
        }

        private sealed class Gate(int participants)
        {
            private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _arrived;

            public bool Opened => _opened.Task.IsCompletedSuccessfully;

            public async Task WaitAsync()
            {
                if (Interlocked.Increment(ref _arrived) >= participants)
                {
                    _opened.TrySetResult();
                }

                // A run that fails before arriving must not hang its peer.
                await Task.WhenAny(_opened.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            }
        }

        private sealed class FakeGraphRepository(AgentSourcePage[] pages, Gate? gate = null) : IAgentGraphRepository
        {
            public async Task<AgentSourcePage> ReadPageAsync(Guid runId, string? nextLink, CancellationToken cancellationToken)
            {
                if (nextLink == null && gate != null)
                {
                    await gate.WaitAsync();
                }

                var index = nextLink == null
                    ? 0
                    : Array.FindIndex(pages, page => page.NextLink == nextLink) + 1;
                return pages[index];
            }
        }
    }
}
