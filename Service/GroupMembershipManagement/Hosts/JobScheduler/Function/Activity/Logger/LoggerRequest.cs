// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Hosts.JobScheduler
{
    public class LoggerRequest
    {
        public string Message { get; set; }
        public Guid RunId { get; set; }

    }
}
