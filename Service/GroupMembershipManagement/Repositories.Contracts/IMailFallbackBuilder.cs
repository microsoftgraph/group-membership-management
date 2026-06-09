// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IMailFallbackBuilder
    {
        Task<string> BuildSyncStartedFallbackAsync(
            EmailMessage emailMessage, string destinationGroupName, string groupId, string jobUrl, string sentDate);

        Task<string> BuildSyncCompletedFallbackAsync(
            EmailMessage emailMessage, string destinationGroupName, string groupId, string jobUrl, string sentDate);
    }
}
