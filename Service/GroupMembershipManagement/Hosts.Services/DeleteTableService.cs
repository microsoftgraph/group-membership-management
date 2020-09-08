// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.Cosmos.Table;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.Services
{
    public class DeleteTableService : IDeleteTableService
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly string _connectionString = null;

        public DeleteTableService(ILoggingRepository loggingRepository, IKeyVaultSecret<DeleteTableService> connectionString)
        {
            _connectionString = connectionString.Secret;
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        public async Task DeleteTableAsync()
        {            
            var currentTimestamp = new DateTimeOffset(DateTime.UtcNow);

            var previousTimestamp = currentTimestamp.AddMonths(-1);
            var _ = LogMessageAsync($"Timestamp to compare with: {previousTimestamp}");

            var timestampFromTable = currentTimestamp;

            var storageAccount = CloudStorageAccount.Parse(_connectionString);
            var tableClient = storageAccount.CreateCloudTableClient();
            var tables = tableClient.ListTables();
            foreach (var table in tables)
            {
                _ = LogMessageAsync($"Checking Timestamp value in Table {table.Name}");
                var query = new TableQuery<PersonEntity>();
                foreach (var message in table.ExecuteQuery(query))
                {
                    timestampFromTable = message.Timestamp;
                    _ = LogMessageAsync($"Timestamp from Table {table.Name}: {timestampFromTable}");
                    break;
                }

                if (timestampFromTable < previousTimestamp)
                {
                    _ = LogMessageAsync($"Table {table.Name} was created a month before");
                    _ = LogMessageAsync($"Deleting Table {table.Name}");
                    await table.DeleteIfExistsAsync();
                    _ = LogMessageAsync($"Deleted Table {table.Name}");
                }
                else
                {
                    _ = LogMessageAsync($"Table {table.Name} was created recently");
                }
            }       
        }

        private async Task LogMessageAsync(string message) =>
           await _loggingRepository.LogMessageAsync(new LogMessage { Message = message });
    }
}

