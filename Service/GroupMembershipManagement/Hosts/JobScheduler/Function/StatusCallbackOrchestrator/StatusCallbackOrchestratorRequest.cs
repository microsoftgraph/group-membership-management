// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using System.Net.Http;

namespace Hosts.JobScheduler
{
    public class StatusCallbackOrchestratorRequest
    {
        public string JobSchedulerStatusUrl { get; set; }
        public string AuthToken { get; set; }
        public string SuccessBody { get; set; }
        public string CallbackUrl { get; set; }
    }
}
