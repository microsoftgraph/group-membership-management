// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models.AdfRun;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface IAdfRunRepository
    {
        /// <summary>
        /// Gets an ADF run record by its pipeline run ID
        /// </summary>
        /// <param name="adfRunId">The ADF pipeline run identifier</param>
        /// <returns>ADF run record or null if not found</returns>
        Task<AdfRun?> GetByAdfRunIdAsync(string adfRunId);
    }
}
