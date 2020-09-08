// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ILoggingRepository
    {
        Task LogMessageAsync(LogMessage logMessage);
    }
}
