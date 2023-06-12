// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Models;
using Repositories.Contracts;
using System;
using System.Threading.Tasks;

namespace Hosts.JobTrigger
{
    public class StarterFunction
    {
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly IDatabaseMigrationsRepository _databaseMigrationsRepository = null;

        public StarterFunction(ILoggingRepository loggingRepository, IDatabaseMigrationsRepository databaseMigrationsRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _databaseMigrationsRepository = databaseMigrationsRepository ?? throw new ArgumentNullException(nameof(databaseMigrationsRepository));
        }


        [FunctionName(nameof(StarterFunction))]
        public async Task Run(
            [TimerTrigger("%jobTriggerSchedule%")] TimerInfo myTimer,
            [DurableClient] IDurableOrchestrationClient starter)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function started" }, VerbosityLevel.DEBUG);
            await starter.StartNewAsync(nameof(OrchestratorFunction), null);
            await _databaseMigrationsRepository.MigrateDatabaseAsync();
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(StarterFunction)} function completed" }, VerbosityLevel.DEBUG);
        }
    }
}

