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
    public class ColumnReaderFunction
    {
        private readonly SqlDataCheckerValidatorService _sqlDataCheckerValidator = null;
        private readonly ILoggingRepository _loggingRepository = null;

        public ColumnReaderFunction(SqlDataCheckerValidatorService sqlDataCheckerValidator, ILoggingRepository loggingRepository)
        {
            _sqlDataCheckerValidator = sqlDataCheckerValidator ?? throw new ArgumentNullException(nameof(sqlDataCheckerValidator));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(ColumnReaderFunction))]
        public async Task<List<string>> GetColumns([ActivityTrigger] string tableName)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ColumnReaderFunction)} function started" }, VerbosityLevel.DEBUG);

            var columns = _sqlDataCheckerValidator.GetColumns(tableName);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ColumnReaderFunction)} function completed" }, VerbosityLevel.DEBUG);

            return columns;
        }
    }
}
