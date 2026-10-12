// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Services.Contracts;
using Services.Entities;
using System;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class DemoAgentBlueprintFunction
    {
        private readonly IDemoAgentGraphService _graphService;

        public DemoAgentBlueprintFunction(IDemoAgentGraphService graphService)
        {
            _graphService = graphService ?? throw new ArgumentNullException(nameof(graphService));
        }

        [Function(nameof(DemoAgentBlueprintFunction))]
        public Task<string> RunAsync([ActivityTrigger] DemoAgentWriterRequest request)
            => _graphService.EnsureBlueprintAsync(sponsorId: request.SponsorId);
    }
}
