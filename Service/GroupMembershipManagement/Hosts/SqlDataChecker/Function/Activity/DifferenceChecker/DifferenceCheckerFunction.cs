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

        private const double NullThresholdPercentage = 0.50;

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
                    var difference = item.Value - previousValue;
                    var currentNullPct = latestNumberOfRows > 0 ? Math.Round((double)item.Value / latestNumberOfRows * 100, 2) : 0;
                    var previousNullPct = previousNumberOfRows > 0 ? Math.Round((double)previousValue / previousNumberOfRows * 100, 2) : 0;
                    var pctDifference = Math.Round(currentNullPct - previousNullPct, 2);
                    TrackNullColumnsEvent(item.Key, item.Value, previousValue, latestNumberOfRows, previousNumberOfRows);
                    await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Column: {item.Key} | Current NULLs: {item.Value}/{latestNumberOfRows} ({currentNullPct}%) | Previous NULLs: {previousValue}/{previousNumberOfRows} ({previousNullPct}%) | Diff: {(difference >= 0 ? "+" : "")}{difference} ({(pctDifference >= 0 ? "+" : "")}{pctDifference}%)" }, VerbosityLevel.INFO);
                }
            }

            var columnsExceedingThreshold = new List<string>();
            var columnThresholds = request.ColumnThresholds ?? new Dictionary<string, double>();

            if (latestColumns != null && latestNumberOfRows > 0)
            {
                foreach (var item in latestColumns)
                {
                    var threshold = columnThresholds.ContainsKey(item.Key)
                        ? columnThresholds[item.Key]
                        : NullThresholdPercentage;

                    if ((long)item.Value * 100 > (long)latestNumberOfRows * (long)(threshold * 100))
                    {
                        var nullPercentage = Math.Round((double)item.Value / latestNumberOfRows * 100, 2);
                        columnsExceedingThreshold.Add($"{item.Key} ({nullPercentage}% null, threshold: {threshold * 100}%)");
                    }
                }
            }
            else if (latestColumns == null || latestNumberOfRows == 0)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = "Latest table has no data or columns — skipping null threshold check." }, VerbosityLevel.INFO);
            }

            if (columnsExceedingThreshold.Count > 0)
            {
                var message = $"SqlDataChecker FAILED: {columnsExceedingThreshold.Count} column(s) exceeded the {NullThresholdPercentage * 100}% null threshold: {string.Join(", ", columnsExceedingThreshold)}";
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = message }, VerbosityLevel.INFO);
                _telemetryClient.TrackEvent("SqlDataCheckerNullThresholdExceeded", new Dictionary<string, string>
                {
                    { "ColumnsExceeded", string.Join(", ", columnsExceedingThreshold) },
                    { "Threshold", $"{NullThresholdPercentage * 100}%" },
                    { "LatestRowCount", latestNumberOfRows.ToString() }
                });
                throw new InvalidOperationException(message);
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