// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class UserReaderSubOrchestratorFunction
    {
        [Function(nameof(UserReaderSubOrchestratorFunction))]
        public async Task<List<GraphProfileInformation>> RunOrchestrator(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger("AzureUserReader.UserReaderSubOrchestratorFunction");
            logger.FunctionStarted(nameof(UserReaderSubOrchestratorFunction));

            var users = new List<GraphProfileInformation>();

            try
            {
                var personnelNumbers = context.GetInput<List<string>>();

                List<string> batch;
                int skip = 0, take = 1000, leftToProcess = personnelNumbers.Count;

                var readerTasks = new List<Task<IList<GraphProfileInformation>>>();

                while ((batch = personnelNumbers.Skip(skip).Take(take).ToList()).Any())
                {
                    readerTasks.Add(context.CallActivityAsync<IList<GraphProfileInformation>>(nameof(AzureUserReaderFunction), batch));

                    if (readerTasks.Count == 5 || leftToProcess <= take)
                    {
                        var results = await Task.WhenAll(readerTasks);
                        users.AddRange(results.SelectMany(x => x));
                        readerTasks.Clear();

                        logger.UsersRetrievedSoFar(users.Count);
                    }

                    skip += take;
                    leftToProcess -= take;
                }

                logger.TotalUsersRetrieved(users.Count);
            }
            catch (Exception ex)
            {
                logger.FunctionFailed(nameof(UserReaderSubOrchestratorFunction), ex);
            }

            logger.FunctionCompleted(nameof(UserReaderSubOrchestratorFunction));

            return users;
        }
    }
}