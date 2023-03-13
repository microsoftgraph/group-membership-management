// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Repositories.Contracts.InjectConfig;

namespace DIConcreteTypes
{
    public class NotificationRepoCredentials<T> : INotificationRepoCredentials<T>
    {
        public string ConnectionString { get; set; }
        public string TableName { get; set; }

        public NotificationRepoCredentials(string connectionString, string tableName)
        {
            this.ConnectionString = connectionString;
            this.TableName = tableName;
        }
        public NotificationRepoCredentials()
        {

        }
    }
}
