// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Hosts.AzureUserReader
{
    public class UserReaderSubOrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;

        public UserReaderSubOrchestratorFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(UserReaderSubOrchestratorFunction))]
        public async Task<List<GraphProfileInformation>> RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(UserReaderSubOrchestratorFunction)} function started" }, VerbosityLevel.DEBUG);

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

                        if (!context.IsReplaying)
                            _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Retrieved {users.Count} users so far!" });
                    }

                    skip += take;
                    leftToProcess -= take;
                }

                if (!context.IsReplaying)
                    _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Retrieved a total of {users.Count} users" });
            }
            catch (Exception ex)
            {
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(UserReaderSubOrchestratorFunction)} failed with exception:\n{ex}" });
            }

            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(UserReaderSubOrchestratorFunction)} function completed" }, VerbosityLevel.DEBUG);

            return users;
        }
    }
}