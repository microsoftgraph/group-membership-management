// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using Hosts.AgentReader.Services.Contracts;
using Microsoft.Data.SqlClient;
using System;
using System.Data.Common;

namespace Hosts.AgentReader.Services
{
    public sealed class AgentSqlConnectionFactory : IAgentSqlConnectionFactory
    {
        private readonly string _connectionString;

        public AgentSqlConnectionFactory(string connectionString)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            _connectionString = connectionString;
        }

        public DbConnection CreateConnection() => new SqlConnection(_connectionString);
    }
}
