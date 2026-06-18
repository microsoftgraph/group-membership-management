// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IAdfRunRepository
    {
        /// <summary>
        /// Gets the custom notes for the specified set of ADF run identifiers.
        /// Only records with a non-empty Notes value are returned.
        /// </summary>
        /// <param name="adfRunIds">The ADF pipeline run identifiers to look up.</param>
        /// <returns>A dictionary mapping ADF run identifier to its Notes value.</returns>
        Task<IReadOnlyDictionary<string, string>> GetNotesByAdfRunIdsAsync(IEnumerable<string> adfRunIds);
    }
}
