// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Services.Contracts;
using Services.Entities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class DemoAgentReaderFunction
    {
        private readonly IDemoAgentGraphService _graphService;

        public DemoAgentReaderFunction(IDemoAgentGraphService graphService)
        {
            _graphService = graphService ?? throw new ArgumentNullException(nameof(graphService));
        }

        [Function(nameof(DemoAgentReaderFunction))]
        public Task<List<ExistingDemoAgent>> RunAsync([ActivityTrigger] object input)
            => _graphService.GetDemoAgentsAsync();
    }
}
