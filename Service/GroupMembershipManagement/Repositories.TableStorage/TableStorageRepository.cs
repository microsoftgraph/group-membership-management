// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Microsoft.Azure.Cosmos.Table;
using System;

namespace Repositories.TableStorage
{
    public class TableStorageRepository : ITableStorageRepository
    {
        private readonly string _connectionString;

        public TableStorageRepository(IKeyVaultSecret<TableStorageRepository> connectionString)
        {
            _connectionString = connectionString.Secret;
        }

        public string GetAccountSASToken()
        {
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(_connectionString);

            SharedAccessAccountPolicy policy = new SharedAccessAccountPolicy()
            {
                Permissions = SharedAccessAccountPermissions.Read,
                Services = SharedAccessAccountServices.Table,
                ResourceTypes = SharedAccessAccountResourceTypes.Service | SharedAccessAccountResourceTypes.Container | SharedAccessAccountResourceTypes.Object,
                SharedAccessStartTime = DateTime.UtcNow.AddMinutes(-5),
                SharedAccessExpiryTime = DateTime.UtcNow.AddHours(24),
                Protocols = SharedAccessProtocol.HttpsOnly
            };

            return storageAccount.GetSharedAccessSignature(policy);
        }
    }
}
