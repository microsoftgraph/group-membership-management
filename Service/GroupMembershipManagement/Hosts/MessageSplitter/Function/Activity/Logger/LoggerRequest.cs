// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;

namespace Hosts.MessageSplitter
{
    public class LoggerRequest
    {
        public LogMessage Message { get; set; }
        public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.INFO;
    }
}
