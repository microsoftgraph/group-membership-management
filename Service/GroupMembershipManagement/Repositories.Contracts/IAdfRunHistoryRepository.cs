// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.AdfRunHistory;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    /// <summary>
    /// Resolves eligible ADF runs from the durable SQL run-history record.
    /// </summary>
    public interface IAdfRunHistoryRepository
    {
        /// <summary>
        /// Gets the latest eligible ADF run using deterministic ordering,
        /// or <see langword="null"/> when no eligible run has been published.
        /// </summary>
        Task<AdfRunHistory?> GetLatestAdfRunAsync(
            string? correlationId = null,
            CancellationToken cancellationToken = default);
    }
}
