// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockLoggingRepository : ILoggingRepository
    {
        public int MessagesLogged { get; private set; } = 0;
        
        public Dictionary<string, string> SyncJobProperties { get; set; }

        public Task LogMessageAsync(LogMessage logMessage)
        {
            MessagesLogged++;
            return Task.CompletedTask;
        }
    }
}
