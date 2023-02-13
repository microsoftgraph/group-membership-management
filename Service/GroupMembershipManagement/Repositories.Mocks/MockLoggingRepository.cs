// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
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
        public Dictionary<Guid, LogProperties> SyncJobPropertiesHistory { get; set; } = new Dictionary<Guid, LogProperties>();
        public bool DryRun { get; set; }

        public void SetSyncJobProperties(Guid key, Dictionary<string, string> properties)
        {
            if (!SyncJobPropertiesHistory.ContainsKey(key))
            {
                SyncJobPropertiesHistory.Add(key, new LogProperties { Properties = properties, ConcurrentParts = 1 });
            }

            if (SyncJobProperties.ContainsKey(key))
            {
                SyncJobProperties[key].Properties = properties;
                SyncJobProperties[key].ConcurrentParts += 1;
            }
            else
            {
                SyncJobProperties.Add(key, new LogProperties { Properties = properties, ConcurrentParts = 1 });
            }
        }

        public void RemoveSyncJobProperties(Guid key)
        {
            if (SyncJobProperties.ContainsKey(key))
            {
                if ((SyncJobProperties[key].ConcurrentParts - 1) <= 0)
                    SyncJobProperties.Remove(key);
                else
                    SyncJobProperties[key].ConcurrentParts -= 1;
            }
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
