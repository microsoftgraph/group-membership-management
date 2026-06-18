// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.AdfRun;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IAdfRunRepository
    {
        /// <summary>
        /// Gets the ADF run record for the specified ADF run identifier.
        /// </summary>
        /// <param name="adfRunId">The ADF pipeline run identifier.</param>
        /// <returns>The matching <see cref="AdfRun"/> or null if not found.</returns>
        Task<AdfRun?> GetByAdfRunIdAsync(string adfRunId);

        /// <summary>
        /// Gets the active custom notes for the specified set of ADF run identifiers.
        /// Only records flagged as active with a non-empty Notes value are returned.
        /// </summary>
        /// <param name="adfRunIds">The ADF pipeline run identifiers to look up.</param>
        /// <returns>A dictionary mapping ADF run identifier to its active Notes value.</returns>
        Task<IReadOnlyDictionary<string, string>> GetActiveNotesByAdfRunIdsAsync(IEnumerable<string> adfRunIds);
    }
}
