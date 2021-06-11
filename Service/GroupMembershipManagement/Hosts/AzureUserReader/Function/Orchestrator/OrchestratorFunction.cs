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
    public class OrchestratorFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;

        public OrchestratorFunction(ILoggingRepository loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [FunctionName(nameof(OrchestratorFunction))]
        public async Task RunOrchestrator(
            [OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function started" });

            try
            {
                var request = context.GetInput<AzureUserReaderRequest>();
                var personnelNumbers = await context.CallActivityAsync<IList<string>>(nameof(PersonnelNumberReaderFunction), request);

                List<string> batch;
                int skip = 0, take = 1000, leftToProcess = personnelNumbers.Count;
                var users = new List<GraphProfileInformation>();
                var readerTasks = new List<Task<IList<GraphProfileInformation>>>();

                while ((batch = personnelNumbers.Skip(skip).Take(take).ToList()).Any())
                {
                    readerTasks.Add(context.CallActivityAsync<IList<GraphProfileInformation>>(nameof(AzureUserReaderFunction), batch));

                    if (readerTasks.Count == 5 || leftToProcess <= take)
                    {
                        var results = await Task.WhenAll(readerTasks);
                        users.AddRange(results.SelectMany(x => x));
                        readerTasks.Clear();
                    }

                    skip += take;
                    leftToProcess -= take;
                }

                if (users.Count > 0)
                    await context.CallActivityAsync(nameof(UploadUsersFunction), new UploadUsersRequest { AzureUserReaderRequest = request, Users = users });
            }
            catch (Exception ex)
            {
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} failed with exception:\n{ex}" });
                throw;
            }

            if (!context.IsReplaying)
                _ = _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(OrchestratorFunction)} function completed" });
        }
    }
}