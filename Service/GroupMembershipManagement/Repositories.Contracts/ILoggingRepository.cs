// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ILoggingRepository
    {
        public Dictionary<string, string> SyncJobProperties { get; set; }
        public bool DryRun { get; set; }
        Task LogMessageAsync(LogMessage logMessage, [CallerMemberName] string caller = "", [CallerFilePath] string file = "");
    }
}
