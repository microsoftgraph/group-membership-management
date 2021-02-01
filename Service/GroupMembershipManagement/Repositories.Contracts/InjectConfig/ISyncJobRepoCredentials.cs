// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
namespace Repositories.Contracts.InjectConfig
{
    public interface ISyncJobRepoCredentials<TType>
    {        
        string ConnectionString { get; }
        string TableName { get; }
    }
}
