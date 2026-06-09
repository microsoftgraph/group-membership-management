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
    public class ThresholdReaderFunction
    {
        private readonly SqlDataCheckerValidatorService _sqlDataCheckerValidator;
        private readonly ILogger<ThresholdReaderFunction> _logger;
        private readonly TelemetryClient _telemetryClient;

        public ThresholdReaderFunction(SqlDataCheckerValidatorService sqlDataCheckerValidator, ILogger<ThresholdReaderFunction> logger, TelemetryClient telemetryClient)
        {
            _sqlDataCheckerValidator = sqlDataCheckerValidator;
            _logger = logger;
            _telemetryClient = telemetryClient;
        }

        [Function(nameof(ThresholdReaderFunction))]
        public Task<Dictionary<string, double>> GetColumnThresholds([ActivityTrigger] object obj)
        {
            _logger.FunctionStarted(nameof(ThresholdReaderFunction));

            try
            {
                var thresholds = _sqlDataCheckerValidator.GetColumnThresholds();

                _logger.ThresholdReaderCompleted(nameof(ThresholdReaderFunction), thresholds.Count);

                return Task.FromResult(thresholds);
            }
            catch (Exception ex)
            {
                _logger.ThresholdReaderFailed(nameof(ThresholdReaderFunction), ex);

                _telemetryClient.TrackException(ex, new Dictionary<string, string>
                {
                    { "Function", nameof(ThresholdReaderFunction) }
                });

                throw;
            }
        }
    }
}
