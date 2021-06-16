// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockLoggingRepository : ILoggingRepository
    {
        public List<LogMessage> MessagesLogged { get; set; } = new List<LogMessage>();
        public int MessagesLoggedCount => MessagesLogged.Count;

        public Dictionary<string, string> SyncJobProperties { get; set; }

        public Task LogMessageAsync(LogMessage logMessage, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
        {
            MessagesLogged.Add(logMessage);
            return Task.CompletedTask;
        }
    }
}
