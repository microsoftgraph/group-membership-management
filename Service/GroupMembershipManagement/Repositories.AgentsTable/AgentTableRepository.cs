// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using Models;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.AgentsTable
{
    public sealed class AgentTableRepository : IAgentTableRepository
    {
        private const int BatchSize = 500;
        private const int CommandTimeoutSeconds = 120;
        private readonly IAgentSqlConnectionFactory _connectionFactory;
        private readonly ILogger<AgentTableRepository> _logger;

        public AgentTableRepository(IAgentSqlConnectionFactory connectionFactory, ILogger<AgentTableRepository> logger)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AgentTableInfo> ValidateTableAsync(Guid runId, CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginRunIdScope(runId);
            var table = GetTableName(runId);
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            int? readiness = null;
            int? managerAllowsNull = null;
            using (var command = connection.CreateCommand())
            {
                command.CommandTimeout = CommandTimeoutSeconds;
                command.CommandText = """
                    SELECT CASE
                        WHEN OBJECT_ID(@tableName, N'U') IS NULL THEN 0
                        WHEN NOT EXISTS (
                            SELECT 1 FROM sys.indexes
                            WHERE object_id = OBJECT_ID(@tableName, N'U')
                              AND name = N'IDX_Agents_AgentManagerId'
                              AND is_disabled = 0 AND is_hypothetical = 0
                        ) THEN 1
                        ELSE 2
                    END,
                    COLUMNPROPERTY(OBJECT_ID(@tableName, N'U'), N'AgentManagerId', 'AllowsNull');
                    """;
                AddParameter(command, "@tableName", DbType.String, table, 128);
                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    readiness = reader.GetInt32(0);
                    managerAllowsNull = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                }
            }

            if (readiness != 2)
            {
                throw new InvalidOperationException("The run's agents table and enabled IDX_Agents_AgentManagerId index must already exist.");
            }

            if (managerAllowsNull is null)
            {
                throw new InvalidOperationException("The run's agents table has no AgentManagerId column.");
            }

            if (await ReadRowCountAsync(connection, table, cancellationToken) != 0)
            {
                throw new InvalidOperationException("A new AgentReader orchestration requires an empty agents table.");
            }

            return new AgentTableInfo(AgentManagerIdAllowsNull: managerAllowsNull == 1);
        }

        public async Task<int> WriteAsync(Guid runId, IReadOnlyList<AgentRecord> agents, CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginRunIdScope(runId);
            var table = GetTableName(runId);
            ArgumentNullException.ThrowIfNull(agents);
            var identifiers = new HashSet<Guid>();
            foreach (var agent in agents)
            {
                if (agent.AgentObjectId == Guid.Empty || agent.BlueprintId?.Length > 200 || !identifiers.Add(agent.AgentObjectId))
                {
                    throw new InvalidDataException("An agent write contains invalid or duplicate rows.");
                }
            }

            if (agents.Count == 0)
            {
                return 0;
            }

            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
            var existing = new Dictionary<Guid, AgentRecord>();

            // Durable activities are at-least-once; the destination has no unique key.
            // Hold a per-table lock across each page's check/insert, not across the whole enumeration.
            foreach (var batch in agents.Chunk(BatchSize))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                var names = new List<string>(batch.Length);
                for (var index = 0; index < batch.Length; index++)
                {
                    var name = $"@id{index}";
                    names.Add(name);
                    AddParameter(command, name, DbType.String, batch[index].AgentObjectId.ToString("D"), 36);
                }

                command.CommandText = $"""
                    SELECT AgentObjectId, AgentManagerId, Blueprint_Code, AgentAccountEnabled
                    FROM {table} WITH (TABLOCKX, HOLDLOCK)
                    WHERE AgentObjectId IN ({string.Join(", ", names)});
                    """;
                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new AgentRecord(
                        Guid.Parse(reader.GetString(0)), reader.IsDBNull(1) ? null : reader.GetInt32(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetBoolean(3));
                    if (!existing.TryAdd(row.AgentObjectId, row))
                    {
                        throw new InvalidDataException("The agents table already contains duplicate agent identifiers.");
                    }
                }
            }

            var pending = new List<AgentRecord>(agents.Count - existing.Count);
            foreach (var agent in agents)
            {
                if (!existing.TryGetValue(agent.AgentObjectId, out var previous))
                {
                    pending.Add(agent);
                }
                else if (previous != agent)
                {
                    throw new InvalidDataException("An existing agent row does not match the redelivered activity input.");
                }
            }

            foreach (var batch in pending.Chunk(BatchSize))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandTimeout = CommandTimeoutSeconds;
                var values = new List<string>(batch.Length);
                for (var index = 0; index < batch.Length; index++)
                {
                    values.Add($"(@id{index}, @manager{index}, @blueprint{index}, @enabled{index})");
                    AddParameter(command, $"@id{index}", DbType.String, batch[index].AgentObjectId.ToString("D"), 36);
                    AddParameter(command, $"@manager{index}", DbType.Int32, batch[index].AgentManagerId);
                    AddParameter(command, $"@blueprint{index}", DbType.String, batch[index].BlueprintId, 200);
                    AddParameter(command, $"@enabled{index}", DbType.Boolean, batch[index].AgentAccountEnabled);
                }

                command.CommandText = $"""
                    INSERT INTO {table} (AgentObjectId, AgentManagerId, Blueprint_Code, AgentAccountEnabled)
                    VALUES {string.Join(", ", values)};
                    """;
                if (await command.ExecuteNonQueryAsync(cancellationToken) != batch.Length)
                {
                    throw new InvalidDataException("SQL did not confirm all agent inserts.");
                }
            }

            await transaction.CommitAsync(cancellationToken);
            _logger.WriteConfirmed(agents.Count, existing.Count);
            return agents.Count;
        }

        public async Task<long> GetRowCountAsync(Guid runId, CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginRunIdScope(runId);
            var table = GetTableName(runId);
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            return await ReadRowCountAsync(connection, table, cancellationToken);
        }

        public async Task<IReadOnlyList<string>> GetBlueprintCodesAsync(Guid runId, CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginRunIdScope(runId);
            var table = GetTableName(runId);
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = $"SELECT DISTINCT Blueprint_Code FROM {table} WHERE Blueprint_Code IS NOT NULL AND Blueprint_Code <> N'';";
            var codes = new List<string>();
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                codes.Add(reader.GetString(0));
            }

            return codes;
        }

        public async Task WriteMappingsAsync(
            Guid runId, string columnName, IReadOnlyDictionary<string, string> descriptionsByCode, CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginRunIdScope(runId);
            ArgumentException.ThrowIfNullOrWhiteSpace(columnName);
            ArgumentNullException.ThrowIfNull(descriptionsByCode);
            if (runId == Guid.Empty)
            {
                throw new ArgumentException("A non-empty RunId is required.", nameof(runId));
            }

            var table = $"[mappings].[{runId:N}]";
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

            using (var check = connection.CreateCommand())
            {
                check.Transaction = transaction;
                check.CommandTimeout = CommandTimeoutSeconds;
                check.CommandText = "SELECT OBJECT_ID(@tableName, N'U');";
                AddParameter(check, "@tableName", DbType.String, table, 128);
                if (await check.ExecuteScalarAsync(cancellationToken) is null or DBNull)
                {
                    throw new InvalidOperationException("The run's mappings table must exist before mappings are written.");
                }
            }

            // Durable activities are at-least-once, so replace rather than append.
            using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandTimeout = CommandTimeoutSeconds;
                delete.CommandText = $"DELETE FROM {table} WHERE ColumnName = @column;";
                AddParameter(delete, "@column", DbType.String, columnName, 128);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var batch in descriptionsByCode.Chunk(BatchSize))
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandTimeout = CommandTimeoutSeconds;
                var values = new List<string>(batch.Length);
                for (var index = 0; index < batch.Length; index++)
                {
                    values.Add($"(@code{index}, @description{index}, @column)");
                    AddParameter(insert, $"@code{index}", DbType.String, batch[index].Key, 200);
                    AddParameter(insert, $"@description{index}", DbType.String, batch[index].Value, 256);
                }

                AddParameter(insert, "@column", DbType.String, columnName, 128);
                insert.CommandText = $"INSERT INTO {table} (Code, Description, ColumnName) VALUES {string.Join(", ", values)};";
                if (await insert.ExecuteNonQueryAsync(cancellationToken) != batch.Length)
                {
                    throw new InvalidDataException("SQL did not confirm all blueprint mapping inserts.");
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }

        private static string GetTableName(Guid runId)
        {
            if (runId == Guid.Empty)
            {
                throw new ArgumentException("A non-empty RunId is required.", nameof(runId));
            }

            return $"[agents].[{runId:N}]";
        }

        private static async Task<long> ReadRowCountAsync(DbConnection connection, string table, CancellationToken cancellationToken)
        {
            using var command = connection.CreateCommand();
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = $"SELECT COUNT_BIG(*) FROM {table};";
            return await command.ExecuteScalarAsync(cancellationToken) is long count
                ? count
                : throw new InvalidDataException("SQL did not return an agent row count.");
        }

        private static void AddParameter(DbCommand command, string name, DbType type, object? value, int? size = null)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.DbType = type;
            parameter.Value = value ?? DBNull.Value;
            if (size.HasValue)
            {
                parameter.Size = size.Value;
            }

            command.Parameters.Add(parameter);
        }
    }
}
