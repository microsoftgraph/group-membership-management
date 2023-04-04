// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Repositories.Contracts;

namespace Hosts.OwnershipReader
{
    public class LoggerRequest
    {
        public string Message { get; set; }
        public SyncJob SyncJob { get; set; }
        public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.INFO;
    }
}