// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Data.Common;

namespace Repositories.Contracts
{
    public interface IAgentSqlConnectionFactory
    {
        DbConnection CreateConnection();
    }
}
