// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Hosts.WebAppFunction;

[assembly: FunctionsStartup(typeof(Startup))]

namespace Hosts.WebAppFunction
{
    public class Startup : FunctionsStartup
    {

        public override void Configure(IFunctionsHostBuilder builder)
        {
            
        }
    }
}
