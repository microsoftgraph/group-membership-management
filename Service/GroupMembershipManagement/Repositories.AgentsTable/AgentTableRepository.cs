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

        public async Task ValidateTableAsync(Guid runId, CancellationToken cancellationToken)
        {
            using var scope = _logger.BeginRunIdScope(runId);
            var table = GetTableName(runId);
            await using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            using var command = connection.CreateCommand();
            command.CommandTimeout = CommandTimeoutSeconds;
            command.CommandText = """
                SELECT CASE
                    WHEN OBJECT_ID(@tableName, N'U') IS NULL THEN 0
                    WHEN NOT EXISTS (
                        SELECT 1 FROM sys.indexes
                        WHERE object_id = OBJECT_ID(@tableName, N'U')
                          AND name = N'IDX_Agents_ManagerId'
                          AND is_disabled = 0 AND is_hypothetical = 0
                    ) THEN 1
                    ELSE 2
                END;
                """;
            AddParameter(command, "@tableName", DbType.String, table, 128);
            var state = await command.ExecuteScalarAsync(cancellationToken);
            if (state is not int readiness || readiness != 2)
            {
                throw new InvalidOperationException("The run's agents table and enabled IDX_Agents_ManagerId index must already exist.");
            }

            if (await ReadRowCountAsync(connection, table, cancellationToken) != 0)
            {
                throw new InvalidOperationException("A new AgentReader orchestration requires an empty agents table.");
            }
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
                    SELECT AgentObjectId, ManagerId, BlueprintId, AccountEnabled
                    FROM {table} WITH (TABLOCKX, HOLDLOCK)
                    WHERE AgentObjectId IN ({string.Join(", ", names)});
                    """;
                using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var row = new AgentRecord(
                        Guid.Parse(reader.GetString(0)), reader.GetInt32(1),
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
                    AddParameter(command, $"@manager{index}", DbType.Int32, batch[index].ManagerId);
                    AddParameter(command, $"@blueprint{index}", DbType.String, batch[index].BlueprintId, 200);
                    AddParameter(command, $"@enabled{index}", DbType.Boolean, batch[index].AccountEnabled);
                }

                command.CommandText = $"""
                    INSERT INTO {table} (AgentObjectId, ManagerId, BlueprintId, AccountEnabled)
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
