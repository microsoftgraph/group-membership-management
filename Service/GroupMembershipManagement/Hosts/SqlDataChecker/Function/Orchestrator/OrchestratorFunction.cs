// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Repositories.Contracts;
using Services.Entities;

namespace SqlDataChecker
{
    public class OrchestratorFunction
    {
        private readonly IConfiguration _configuration;
        private readonly ILoggingRepository _loggingRepository;
        public OrchestratorFunction(IConfiguration configuration, ILoggingRepository loggingRepository)
        {
            _configuration = configuration;
            _loggingRepository = loggingRepository;
        }

        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function started", Verbosity = VerbosityLevel.DEBUG });

            var tableNames = await context.CallActivityAsync<TableName>(nameof(TableNameReaderFunction), null);

            if (!string.IsNullOrWhiteSpace(tableNames.Latest) && !string.IsNullOrWhiteSpace(tableNames.Previous))
            {
                var latestColumns = await context.CallActivityAsync<List<string>>(nameof(ColumnReaderFunction), tableNames.Latest);
                var previousColumns = await context.CallActivityAsync<List<string>>(nameof(ColumnReaderFunction), tableNames.Previous);

                var nullLatestColumns = await context.CallActivityAsync<Dictionary<string, int>>(nameof(ColumnValidatorFunction), new ColumnValidatorRequest { Columns = latestColumns, Table = tableNames.Latest });
                var nullPreviousColumns = await context.CallActivityAsync<Dictionary<string, int>>(nameof(ColumnValidatorFunction), new ColumnValidatorRequest { Columns = previousColumns, Table = tableNames.Previous });

                var numberOfLatestRows = await context.CallActivityAsync<int>(nameof(RowReaderFunction), tableNames.Latest);
                var numberOfPrevioustRows = await context.CallActivityAsync<int>(nameof(RowReaderFunction), tableNames.Previous);

                await context.CallActivityAsync(nameof(DifferenceCheckerFunction), new DifferenceCheckerRequest { LatestNullColumns = nullLatestColumns, PreviousNullColumns = nullPreviousColumns, LatestNumberOfRows = numberOfLatestRows, PreviousNumberOfRows = numberOfPrevioustRows });
            }
            await context.CallActivityAsync(nameof(LoggerFunction), new LoggerRequest { Message = $"{nameof(OrchestratorFunction)} function completed", Verbosity = VerbosityLevel.DEBUG });
        }
    }
}