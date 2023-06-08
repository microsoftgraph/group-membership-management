// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDatabaseMigrationsRepository
    {
        Task MigrateDatabaseAsync();
    }
}
