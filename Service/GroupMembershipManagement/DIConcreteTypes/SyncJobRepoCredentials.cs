// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class SyncJobRepoCredentials<T> : ISyncJobRepoCredentials<T>
    {
        public string ConnectionString { get; set; }
        public string TableName { get; set; }
        public SyncJobRepoCredentials(string connectionString, string tableName)
        {
            this.ConnectionString = connectionString;
            this.TableName = tableName;
        }
        public SyncJobRepoCredentials()
        {

        }
    }
}
