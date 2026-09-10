// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.AgentsTable;
using Repositories.Contracts;
using Hosts.AgentReader.Services.Contracts;
using Hosts.AgentReader.Services.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Hosts.AgentReader.Services.Tests
{
    [TestClass]
    [TestCategory("LocalSqlIntegration")]
    public class AgentTableRepositorySqlTests
    {
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
            _databaseName = $"AgentReaderTests_{Guid.NewGuid():N}";
            await ExecuteAsync(_masterConnectionString, $"CREATE DATABASE [{_databaseName}];");
            _databaseCreated = true;
            builder.InitialCatalog = _databaseName;
            _connectionString = builder.ConnectionString;
            await ExecuteAsync(_connectionString, "CREATE SCHEMA agents;");
            await ExecuteAsync(_connectionString, """
                CREATE USER AgentReaderTestWriter WITHOUT LOGIN;
                ALTER ROLE db_datareader ADD MEMBER AgentReaderTestWriter;
                ALTER ROLE db_datawriter ADD MEMBER AgentReaderTestWriter;
                """);
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
        public async Task ReaderWriterRoles_CanValidateAndLoadButCannotCreateIndexes()
        {
            var runId = Guid.NewGuid();
            await CreateTableAsync(runId);
            var repository = CreateRepository();
            await repository.ValidateTableAsync(runId, CancellationToken.None);
            AgentRecord[] rows =
            [
                new(Guid.NewGuid(), int.MinValue, null, false),
                new(Guid.NewGuid(), int.MaxValue, "", true),
                new(Guid.NewGuid(), 42, " {Unnormalised-BLUEPRINT} ", false),
                new(Guid.NewGuid(), 0, new string('x', 200), true)
            ];

            Assert.AreEqual(rows.Length, await repository.WriteAsync(runId, rows, CancellationToken.None));
            Assert.AreEqual((long)rows.Length, await repository.GetRowCountAsync(runId, CancellationToken.None));
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT AgentObjectId, ManagerId, BlueprintId, AccountEnabled FROM [agents].[{runId:N}];";
            using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var expected = rows.Single(row => row.AgentObjectId.ToString("D") == reader.GetString(0));
                    Assert.AreEqual(expected.ManagerId, reader.GetInt32(1));
                    Assert.AreEqual(expected.BlueprintId, reader.IsDBNull(2) ? null : reader.GetString(2));
                    Assert.AreEqual(expected.AccountEnabled, reader.GetBoolean(3));
                }
            }

            await using var limited = new WriterConnection(_connectionString);
            await limited.OpenAsync(CancellationToken.None);
            using var denied = limited.CreateCommand();
            denied.CommandText = $"CREATE INDEX ForbiddenIndex ON [agents].[{runId:N}](AgentObjectId);";
            await Assert.ThrowsExceptionAsync<SqlException>(() => denied.ExecuteNonQueryAsync());
        }

        [TestMethod]
        public async Task LargePage_StaysBelowParameterLimitAndIsIdempotentOnRedelivery()
        {
            var runId = Guid.NewGuid();
            await CreateTableAsync(runId);
            var repository = CreateRepository();
            var rows = Enumerable.Range(0, 1101)
                .Select(index => new AgentRecord(Guid.NewGuid(), index, index % 2 == 0 ? null : "raw", index % 2 == 0))
                .ToArray();

            Assert.AreEqual(1101, await repository.WriteAsync(runId, rows, CancellationToken.None));
            Assert.AreEqual(1101, await repository.WriteAsync(runId, rows, CancellationToken.None));
            Assert.AreEqual(1101L, await repository.GetRowCountAsync(runId, CancellationToken.None));
            Assert.AreEqual(0, await ScalarAsync<int>($"""
                SELECT COUNT(*) FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[agents].[{runId:N}]') AND is_unique = 1;
                """));
        }

        [TestMethod]
        public async Task ConcurrentRedeliveries_DoNotDuplicateRowsWithoutAUniqueConstraint()
        {
            var runId = Guid.NewGuid();
            await CreateTableAsync(runId);
            var rows = Enumerable.Range(0, 64).Select(index => new AgentRecord(Guid.NewGuid(), index, null, true)).ToArray();

            var results = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => CreateRepository().WriteAsync(runId, rows, CancellationToken.None)));

            Assert.IsTrue(results.All(result => result == 64));
            Assert.AreEqual(64L, await CreateRepository().GetRowCountAsync(runId, CancellationToken.None));
        }

        [TestMethod]
        public async Task ConflictingRedelivery_FailsWithoutChangingExistingRows()
        {
            var runId = Guid.NewGuid();
            await CreateTableAsync(runId);
            var repository = CreateRepository();
            var original = new AgentRecord(Guid.NewGuid(), 1, "Original", false);
            await repository.WriteAsync(runId, [original], CancellationToken.None);

            await Assert.ThrowsExceptionAsync<InvalidDataException>(
                () => repository.WriteAsync(runId, [original with { ManagerId = 2 }], CancellationToken.None));

            Assert.AreEqual(1L, await repository.GetRowCountAsync(runId, CancellationToken.None));
            Assert.AreEqual(1, await ScalarAsync<int>($"SELECT ManagerId FROM [agents].[{runId:N}];"));
        }

        [TestMethod]
        public async Task LaterInsertBatchFailure_RollsBackCurrentPageButNotEarlierPages()
        {
            var runId = Guid.NewGuid();
            await CreateTableAsync(runId);
            await ExecuteAsync(_connectionString, $"ALTER TABLE [agents].[{runId:N}] ADD CHECK (ManagerId < 501);");
            var repository = CreateRepository();
            await repository.WriteAsync(runId, [new AgentRecord(Guid.NewGuid(), 0, null, true)], CancellationToken.None);
            var failingPage = Enumerable.Range(1, 600)
                .Select(index => new AgentRecord(Guid.NewGuid(), index, null, true)).ToArray();

            await Assert.ThrowsExceptionAsync<SqlException>(
                () => repository.WriteAsync(runId, failingPage, CancellationToken.None));

            Assert.AreEqual(1L, await repository.GetRowCountAsync(runId, CancellationToken.None));
            Assert.AreEqual(0, await ScalarAsync<int>($"SELECT ManagerId FROM [agents].[{runId:N}];"));
        }

        [TestMethod]
        public async Task MissingTableOrIndex_FailsWithoutCreatingEither()
        {
            var runId = Guid.NewGuid();
            var repository = CreateRepository();
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => repository.ValidateTableAsync(runId, CancellationToken.None));
            Assert.AreEqual(0, await ScalarAsync<int>(
                $"SELECT CASE WHEN OBJECT_ID(N'[agents].[{runId:N}]', N'U') IS NULL THEN 0 ELSE 1 END;"));

            await CreateTableAsync(runId, createIndex: false);
            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => repository.ValidateTableAsync(runId, CancellationToken.None));
            Assert.AreEqual(0, await ScalarAsync<int>($"""
                SELECT COUNT(*) FROM sys.indexes
                WHERE object_id = OBJECT_ID(N'[agents].[{runId:N}]') AND name = N'IDX_Agents_ManagerId';
                """));
        }

        [TestMethod]
        public async Task NewInstanceOnNonEmptyTable_FailsWithoutDeletingRows()
        {
            var runId = Guid.NewGuid();
            await CreateTableAsync(runId);
            var repository = CreateRepository();
            await repository.WriteAsync(runId, [new AgentRecord(Guid.NewGuid(), 1, null, true)], CancellationToken.None);

            await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => repository.ValidateTableAsync(runId, CancellationToken.None));

            Assert.AreEqual(1L, await repository.GetRowCountAsync(runId, CancellationToken.None));
        }

        [TestMethod]
        public async Task AlreadyDuplicatedTable_FailsWithoutCompensatingDeletes()
        {
            var runId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            await CreateTableAsync(runId);
            await ExecuteAsync(_connectionString, $"""
                INSERT INTO [agents].[{runId:N}] (AgentObjectId, ManagerId, BlueprintId, AccountEnabled)
                VALUES ('{agentId:D}', 1, NULL, 1), ('{agentId:D}', 1, NULL, 1);
                """);
            var repository = CreateRepository();

            await Assert.ThrowsExceptionAsync<InvalidDataException>(
                () => repository.WriteAsync(runId, [new AgentRecord(agentId, 1, null, true)], CancellationToken.None));

            Assert.AreEqual(2L, await repository.GetRowCountAsync(runId, CancellationToken.None));
        }

        private static AgentTableRepository CreateRepository() =>
            new(new WriterConnectionFactory(_connectionString), NullLogger<AgentTableRepository>.Instance);

        private static async Task CreateTableAsync(Guid runId, bool createIndex = true)
        {
            await ExecuteAsync(_connectionString, $"""
                CREATE TABLE [agents].[{runId:N}] (
                    AgentObjectId nvarchar(36) NOT NULL,
                    ManagerId int NOT NULL,
                    BlueprintId nvarchar(200) NULL,
                    AccountEnabled bit NOT NULL
                );
                """);
            if (createIndex)
            {
                await ExecuteAsync(_connectionString,
                    $"CREATE CLUSTERED INDEX IDX_Agents_ManagerId ON [agents].[{runId:N}](ManagerId);");
            }
        }

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        private static async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync();
            return result is T typed ? typed : throw new AssertFailedException("Unexpected SQL scalar type.");
        }

        private sealed class WriterConnectionFactory(string connectionString) : IAgentSqlConnectionFactory
        {
            public DbConnection CreateConnection() => new WriterConnection(connectionString);
        }

        private sealed class WriterConnection(string connectionString) : DbConnection
        {
            private readonly SqlConnection _inner = new(connectionString);
            [AllowNull]
            public override string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
            public override string Database => _inner.Database;
            public override string DataSource => _inner.DataSource;
            public override string ServerVersion => _inner.ServerVersion;
            public override ConnectionState State => _inner.State;
            public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
            public override void Close() => _inner.Close();
            public override void Open() => throw new NotSupportedException("Use OpenAsync.");
            protected override DbCommand CreateDbCommand() => _inner.CreateCommand();
            protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => _inner.BeginTransaction(isolationLevel);
            protected override ValueTask<DbTransaction> BeginDbTransactionAsync(
                IsolationLevel isolationLevel, CancellationToken cancellationToken) =>
                _inner.BeginTransactionAsync(isolationLevel, cancellationToken);

            public override async Task OpenAsync(CancellationToken cancellationToken)
            {
                await _inner.OpenAsync(cancellationToken);
                using var command = _inner.CreateCommand();
                command.CommandText = "EXECUTE AS USER = N'AgentReaderTestWriter';";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _inner.Dispose();
                }
                base.Dispose(disposing);
            }

            public override async ValueTask DisposeAsync()
            {
                await _inner.DisposeAsync();
                GC.SuppressFinalize(this);
            }
        }
    }
}
