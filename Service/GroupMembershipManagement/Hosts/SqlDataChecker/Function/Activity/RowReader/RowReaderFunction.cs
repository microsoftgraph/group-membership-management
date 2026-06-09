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
    public class RowReaderFunction
    {
        private readonly SqlDataCheckerValidatorService _sqlDataCheckerValidator = null;
        private readonly ILoggingRepository _loggingRepository = null;

        public RowReaderFunction(SqlDataCheckerValidatorService sqlDataCheckerValidator, ILoggingRepository loggingRepository)
        {
            _sqlDataCheckerValidator = sqlDataCheckerValidator ?? throw new ArgumentNullException(nameof(sqlDataCheckerValidator));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(RowReaderFunction))]
        public async Task<int> GetRows([ActivityTrigger] string tableName)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(RowReaderFunction)} function started" }, VerbosityLevel.DEBUG);

            var numberOfRows = _sqlDataCheckerValidator.GetRows(tableName);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(RowReaderFunction)} function completed" }, VerbosityLevel.DEBUG);

            return numberOfRows;
        }
    }
}
