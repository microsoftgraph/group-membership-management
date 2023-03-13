// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace Repositories.Contracts.InjectConfig
{
    public interface INotificationRepoCredentials<TType>
    {        
        string ConnectionString { get; }
        string TableName { get; }
    }
}
