// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Repositories.Contracts
{
    public interface ILoggingRepository
    {        
        public Dictionary<string, string> SyncJobProperties { get; set; }
        Task LogMessageAsync(LogMessage logMessage);
    }
}
