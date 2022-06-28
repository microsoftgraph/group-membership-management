// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;

namespace Hosts.WebAppFunction
{
    public class WebAppFunction
    {

        public WebAppFunction()
        {
        }

        [FunctionName("WebAppFunction")]
        public Task<HttpResponseMessage> HttpStart([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req) 
        {
            return null;
        }
    }
}
