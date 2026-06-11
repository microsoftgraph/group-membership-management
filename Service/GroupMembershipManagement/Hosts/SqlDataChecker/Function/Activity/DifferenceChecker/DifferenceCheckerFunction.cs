// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.SqlDataChecker;
using Microsoft.ApplicationInsights;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SqlDataChecker
{
    public class DifferenceCheckerFunction
    {
        private readonly SqlDataCheckerValidatorService _sqlDataCheckerValidator;
        private readonly ILogger<DifferenceCheckerFunction> _logger;
        private readonly TelemetryClient _telemetryClient;

        public DifferenceCheckerFunction(SqlDataCheckerValidatorService sqlDataCheckerValidator, ILogger<DifferenceCheckerFunction> logger, TelemetryClient telemetryClient)
        {
            _sqlDataCheckerValidator = sqlDataCheckerValidator ?? throw new ArgumentNullException(nameof(sqlDataCheckerValidator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        private const double NullThresholdPercentage = 0.50;

        [Function(nameof(DifferenceCheckerFunction))]
        public Task ValidateColumn([ActivityTrigger] DifferenceCheckerRequest request)
        {
            _logger.FunctionStarted(nameof(DifferenceCheckerFunction));

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
                    _logger.ColumnNullComparison(
                        item.Key, item.Value, latestNumberOfRows, currentNullPct,
                        previousValue, previousNumberOfRows, previousNullPct,
                        $"{(difference >= 0 ? "+" : "")}{difference}",
                        $"{(pctDifference >= 0 ? "+" : "")}{pctDifference}");
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

                    var nullRatio = (double)item.Value / latestNumberOfRows;
                    if (nullRatio > threshold)
                    {
                        var nullPercentage = Math.Round(nullRatio * 100, 2);
                        columnsExceedingThreshold.Add($"{item.Key} ({nullPercentage}% null, threshold: {Math.Round(threshold * 100, 2)}%)");
                    }
                }
            }
            else if (latestColumns == null || latestNumberOfRows == 0)
            {
                _logger.SkippingNullThresholdCheck();
            }

            if (columnsExceedingThreshold.Count > 0)
            {
                var message = $"SqlDataChecker FAILED: {columnsExceedingThreshold.Count} column(s) exceeded their null threshold: {string.Join(", ", columnsExceedingThreshold)}";
                _logger.NullThresholdExceeded(columnsExceedingThreshold.Count, string.Join(", ", columnsExceedingThreshold));
                _telemetryClient.TrackEvent("SqlDataCheckerNullThresholdExceeded", new Dictionary<string, string>
                {
                    { "ColumnsExceeded", string.Join(", ", columnsExceedingThreshold) },
                    { "DefaultThreshold", $"{NullThresholdPercentage * 100}%" },
                    { "LatestRowCount", latestNumberOfRows.ToString() }
                });
                throw new InvalidOperationException(message);
            }

            _logger.FunctionCompleted(nameof(DifferenceCheckerFunction));
            return Task.CompletedTask;
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