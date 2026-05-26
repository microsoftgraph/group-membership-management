// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IDataFactoryRepository
    {
        Task<string> GetMostRecentSucceededRunIdAsync();
        Task<(string current, string previousSucceeded)> GetCurrentRunAndPreviousSucceededRunIdsAsync();
    }
}
