// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Microsoft.Azure.Functions.Worker;
using Repositories.Contracts;
using Services;
using Services.Entities;
using System;
using System.Threading.Tasks;

namespace SqlDataChecker
{
    public class TableNameReaderFunction
    {
        private readonly SqlDataCheckerValidatorService _sqlDataCheckerValidator = null;
        private readonly ILoggingRepository _loggingRepository = null;

        public TableNameReaderFunction(SqlDataCheckerValidatorService sqlDataCheckerValidator, ILoggingRepository loggingRepository)
        {
            _sqlDataCheckerValidator = sqlDataCheckerValidator ?? throw new ArgumentNullException(nameof(sqlDataCheckerValidator));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(TableNameReaderFunction))]
        public async Task<TableName> GetSqlDataCheckerTableName([ActivityTrigger] object obj)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TableNameReaderFunction)} function started" }, VerbosityLevel.DEBUG);

            var tableNames = await _sqlDataCheckerValidator.GetTableNamesAsync();

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TableNameReaderFunction)} function completed" }, VerbosityLevel.DEBUG);

            return tableNames;
        }
    }
}
