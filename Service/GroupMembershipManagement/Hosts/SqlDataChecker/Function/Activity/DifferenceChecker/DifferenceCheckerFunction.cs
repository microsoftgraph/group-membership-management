// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Repositories.Contracts;
using Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SqlDataChecker
{
    public class DifferenceCheckerFunction
    {
        private readonly SqlDataCheckerValidatorService _sqlDataCheckerValidator = null;
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly TelemetryClient _telemetryClient = null;

        public DifferenceCheckerFunction(SqlDataCheckerValidatorService sqlDataCheckerValidator, ILoggingRepository loggingRepository, TelemetryClient telemetryClient)
        {
            _sqlDataCheckerValidator = sqlDataCheckerValidator ?? throw new ArgumentNullException(nameof(sqlDataCheckerValidator));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [Function(nameof(DifferenceCheckerFunction))]
        public async Task ValidateColumn([ActivityTrigger] DifferenceCheckerRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DifferenceCheckerFunction)} function started" }, VerbosityLevel.DEBUG);

            var latestColumns = request.LatestNullColumns;
            var previousColumns = request.PreviousNullColumns;
            var latestNumberOfRows = request.LatestNumberOfRows;
            var previousNumberOfRows = request.PreviousNumberOfRows;

            if (latestColumns != null && previousColumns != null && latestNumberOfRows > 0 && previousNumberOfRows > 0)
            {
                foreach (var item in latestColumns)
                {
                    var previousValue = previousColumns.ContainsKey(item.Key) ? previousColumns[item.Key] : 0;
                    TrackNullColumnsEvent(item.Key, item.Value, previousValue, latestNumberOfRows, previousNumberOfRows);
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Column Name - {item.Key} and Current # of NULLs - {item.Value} and Previous # of NULLs - {previousValue}" }, VerbosityLevel.INFO);
                }
            }
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(DifferenceCheckerFunction)} function completed" }, VerbosityLevel.DEBUG);
        }

        private void TrackNullColumnsEvent(string columnName, int currentValue, int previousValue, int currentRows, int previousRows)
        {
            var nullColumnsEvent = new Dictionary<string, string>
            {
                { "Column", columnName },
                { "CurrentNull", currentValue.ToString() },
                { "PreviousNull", previousValue.ToString() },
                { "CurrentRows", currentRows.ToString() },
                { "PreviousRows", previousRows.ToString() }
            };
            _telemetryClient.TrackEvent("TrackNullColumns", nullColumnsEvent);
        }
    }
}