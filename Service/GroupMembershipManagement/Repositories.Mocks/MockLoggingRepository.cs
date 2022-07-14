// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Repositories.Mocks
{
    public class MockLoggingRepository : ILoggingRepository
    {
        public List<LogMessage> MessagesLogged { get; set; } = new List<LogMessage>();
        public int MessagesLoggedCount => MessagesLogged.Count;
        public Dictionary<Guid, LogProperties> SyncJobProperties { get; set; } = new Dictionary<Guid, LogProperties>();
        public bool DryRun { get; set; }

        public void SetSyncJobProperties(Guid key, Dictionary<string, string> properties)
        {

        }

        public void RemoveSyncJobProperties(Guid key)
        {

        }

        public Task LogMessageAsync(LogMessage logMessage, VerbosityLevel verbosityLevel = VerbosityLevel.INFO, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
        {
            MessagesLogged.Add(logMessage);
            return Task.CompletedTask;
        }

        public Task LogPIIMessageAsync(LogMessage logMessage, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
        {
            MessagesLogged.Add(logMessage);
            return Task.CompletedTask;
        }
    }
}
