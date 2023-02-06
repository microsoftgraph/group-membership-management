// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Models.Entities;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ILoggingRepository
    {
        public Dictionary<Guid, LogProperties> SyncJobProperties { get; }
        public bool DryRun { get; set; }
        public void SetSyncJobProperties(Guid key, Dictionary<string, string> properties);
        public void RemoveSyncJobProperties(Guid key);
        Task LogMessageAsync(LogMessage logMessage, VerbosityLevel verbosityLevel = VerbosityLevel.INFO, [CallerMemberName] string caller = "", [CallerFilePath] string file = "");
        Task LogPIIMessageAsync(LogMessage logMessage, [CallerMemberName] string caller = "", [CallerFilePath] string file = "");
    }
}
