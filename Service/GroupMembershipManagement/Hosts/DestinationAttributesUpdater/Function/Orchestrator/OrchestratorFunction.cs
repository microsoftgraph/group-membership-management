// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.DestinationAttributesUpdater
{
    public class OrchestratorFunction
    {
        private const int BATCH_SIZE = 20;

        public OrchestratorFunction()
        {
        }

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync([OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger("DestinationAttributesUpdater.OrchestratorFunction");

            logger.FunctionStarted(nameof(OrchestratorFunction));

            try
            {
                var destinationTypes = new List<string> { "GroupMembership", "TeamsChannelMembership" };

                foreach (var destinationType in destinationTypes)
                {
                    var destinationsList = await context.CallActivityAsync<List<DestinationInfo>>(nameof(DestinationReaderFunction), destinationType);

                    int index = 0;
                    while (index < destinationsList.Count)
                    {
                        var batch = destinationsList.Skip(index).Take(BATCH_SIZE).ToList();
                        var attributeReaderRequest = new AttributeReaderRequest { Destinations = batch, DestinationType = destinationType };
                        var destinationAttributesList = await context.CallActivityAsync<List<DestinationAttributes>>(nameof(AttributeReaderFunction), attributeReaderRequest);
                        foreach (var destinationAttributes in destinationAttributesList)
                        {
                            await context.CallActivityAsync(nameof(AttributeCacheUpdaterFunction), destinationAttributes);
                        }
                        index += BATCH_SIZE;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.OrchestratorUnexpectedException(ex);
            }

            logger.FunctionCompleted(nameof(OrchestratorFunction));
        }
    }
}
