// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Repositories.Contracts;
using System.Threading.Tasks;

namespace Tests.Repositories.Common
{
    public class MockLoggingRepository : ILoggingRepository
    {
        public Task LogMessageAsync(LogMessage logMessage)
        {
            return Task.CompletedTask;
        }
    }
}

