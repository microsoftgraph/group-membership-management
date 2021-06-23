// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class SyncJobRepoCredentials<T> : ISyncJobRepoCredentials<T>
    {
        public string ConnectionString { get; set; }
        public string TableName { get; set; }
        public string GlobalDryRun { get; set; }

        public SyncJobRepoCredentials(string connectionString, string tableName, string globalDryRun)
        {
            this.ConnectionString = connectionString;
            this.TableName = tableName;
            this.GlobalDryRun = globalDryRun;
        }
        public SyncJobRepoCredentials()
        {

        }
    }
}
