// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
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

        public ThresholdReaderFunction(SqlDataCheckerValidatorService sqlDataCheckerValidator, ILoggingRepository loggingRepository)
        {
            _sqlDataCheckerValidator = sqlDataCheckerValidator ?? throw new ArgumentNullException(nameof(sqlDataCheckerValidator));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(ThresholdReaderFunction))]
        public async Task<Dictionary<string, double>> GetColumnThresholds([ActivityTrigger] object obj)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ThresholdReaderFunction)} function started" }, VerbosityLevel.DEBUG);

            var thresholds = _sqlDataCheckerValidator.GetColumnThresholds();

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ThresholdReaderFunction)} function completed. Found {thresholds.Count} column-specific threshold(s)." }, VerbosityLevel.DEBUG);

            return thresholds;
        }
    }
}
