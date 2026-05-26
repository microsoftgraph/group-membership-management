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
    public class ThresholdReaderFunction
    {
        private readonly SqlDataCheckerValidatorService _sqlDataCheckerValidator = null;
        private readonly ILoggingRepository _loggingRepository = null;
        private readonly TelemetryClient _telemetryClient = null;

        public ThresholdReaderFunction(SqlDataCheckerValidatorService sqlDataCheckerValidator, ILoggingRepository loggingRepository, TelemetryClient telemetryClient)
        {
            _sqlDataCheckerValidator = sqlDataCheckerValidator ?? throw new ArgumentNullException(nameof(sqlDataCheckerValidator));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        [Function(nameof(ThresholdReaderFunction))]
        public async Task<Dictionary<string, double>> GetColumnThresholds([ActivityTrigger] object obj)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ThresholdReaderFunction)} function started" }, VerbosityLevel.DEBUG);

            try
            {
                var thresholds = _sqlDataCheckerValidator.GetColumnThresholds();

                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ThresholdReaderFunction)} function completed. Found {thresholds.Count} column-specific threshold(s)." }, VerbosityLevel.DEBUG);

                return thresholds;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(
                    new LogMessage { Message = $"{nameof(ThresholdReaderFunction)} failed to read column thresholds: {ex.Message}. Failing pipeline to avoid running with incomplete configuration." },
                    VerbosityLevel.DEBUG);

                _telemetryClient.TrackException(ex, new Dictionary<string, string>
                {
                    { "Function", nameof(ThresholdReaderFunction) }
                });

                throw;
            }
        }
    }
}
