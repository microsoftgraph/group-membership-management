// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.SqlDataChecker;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using System.Collections.Generic;
using System.Threading.Tasks;
using Services.Entities;

namespace SqlDataChecker
{
    public class OrchestratorFunction
    {
        [Function(nameof(OrchestratorFunction))]
        public async Task RunOrchestratorAsync(
            [OrchestrationTrigger] TaskOrchestrationContext context)
        {
            var logger = context.CreateReplaySafeLogger($"SqlDataChecker.{nameof(OrchestratorFunction)}");

            logger.FunctionStarted(nameof(OrchestratorFunction));

            var tableNames = await context.CallActivityAsync<TableName>(nameof(TableNameReaderFunction), null);

            if (!string.IsNullOrWhiteSpace(tableNames.Latest) && !string.IsNullOrWhiteSpace(tableNames.Previous))
            {
                var latestColumns = await context.CallActivityAsync<List<string>>(nameof(ColumnReaderFunction), tableNames.Latest);
                var previousColumns = await context.CallActivityAsync<List<string>>(nameof(ColumnReaderFunction), tableNames.Previous);

                var nullLatestColumns = await context.CallActivityAsync<Dictionary<string, int>>(nameof(ColumnValidatorFunction), new ColumnValidatorRequest { Columns = latestColumns, Table = tableNames.Latest });
                var nullPreviousColumns = await context.CallActivityAsync<Dictionary<string, int>>(nameof(ColumnValidatorFunction), new ColumnValidatorRequest { Columns = previousColumns, Table = tableNames.Previous });

                var numberOfLatestRows = await context.CallActivityAsync<int>(nameof(RowReaderFunction), tableNames.Latest);
                var numberOfPrevioustRows = await context.CallActivityAsync<int>(nameof(RowReaderFunction), tableNames.Previous);

                var columnThresholds = await context.CallActivityAsync<Dictionary<string, double>>(nameof(ThresholdReaderFunction), null);

                await context.CallActivityAsync(nameof(DifferenceCheckerFunction), new DifferenceCheckerRequest { LatestNullColumns = nullLatestColumns, PreviousNullColumns = nullPreviousColumns, LatestNumberOfRows = numberOfLatestRows, PreviousNumberOfRows = numberOfPrevioustRows, ColumnThresholds = columnThresholds });
            }

            logger.FunctionCompleted(nameof(OrchestratorFunction));
        }
    }
}