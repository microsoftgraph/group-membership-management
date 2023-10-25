// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using System.Threading.Tasks;

namespace SqlMembershipObtainer.Common.DependencyInjection
{
    public interface IDataFactoryRepository
    {
        Task<string> GetMostRecentSucceededRunIdAsync();
        Task<(string latest, string previous)> GetTwoRecentSucceededRunIdsAsync();
    }
}
