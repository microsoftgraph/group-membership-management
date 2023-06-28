// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Repositories.Contracts;
using System;

namespace Hosts.TeamsChannelUpdater
{
    public class LoggerRequest
    {
        public string Message { get; set; }
        public Guid RunId { get; set; }
        public VerbosityLevel Verbosity { get; set; } = VerbosityLevel.INFO;

    }
}
