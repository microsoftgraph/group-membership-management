// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Models;
using Repositories.Contracts;
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

            await context.CallActivityAsync(nameof(LoggerFunction),
                new LoggerRequest
                {
                    Message = $"{nameof(OrchestratorFunction)} function started at: {context.CurrentUtcDateTime}",
                    Verbosity = VerbosityLevel.DEBUG
                });

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
                await context.CallActivityAsync(nameof(LoggerFunction),
                   new LoggerRequest
                   {
                       Message = $"An unexpected error occurred.\n{ex}"
                   });
            }

            await context.CallActivityAsync(nameof(LoggerFunction),
               new LoggerRequest
               {
                   Message = $"{nameof(OrchestratorFunction)} function completed at: {context.CurrentUtcDateTime}",
                   Verbosity = VerbosityLevel.DEBUG
               });
        }
    }
}
