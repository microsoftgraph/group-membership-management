// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.


namespace Hosts.JobScheduler
{
    public class PostCallbackRequest
    {
        public string AuthToken { get; set; }
        public string SuccessBody { get; set; }
        public string CallbackUrl { get; set; }
    }
}
