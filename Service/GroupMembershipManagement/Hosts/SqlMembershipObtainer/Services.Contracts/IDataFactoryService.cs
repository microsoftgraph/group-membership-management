// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Threading.Tasks;

namespace Services.Contracts
{
    public interface IDataFactoryService
    {
        Task<string> GetMostRecentSucceededRunIdAsync(Guid? runId);
    }
}
