// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Hosts.GroupTableManager
{
    public class GroupTableManager
    {

        public GroupTableManager()
        {
        }

        [FunctionName("GroupTableManager")]
        public Task<HttpResponseMessage> HttpStart([HttpTrigger(AuthorizationLevel.Function, "post")] HttpRequestMessage req) 
        {
            return null;
        }


        [FunctionName("TestFunc")]
        public string Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] // default value for route is <functionname>
            HttpRequest req, ILogger log)
        {
            // should only return a value after validating the bearer token passed to this
            // see eg in publish apps
            // will only work once graphAppClient is the same appregistration as auth (bc it will be passed from there)
            string val = "yay the get endpoint works!";
            return val;
        }
        
    }
}
