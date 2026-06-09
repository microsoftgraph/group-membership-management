// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.SqlDataChecker;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Services;
using Services.Entities;
using System.Threading.Tasks;

namespace SqlDataChecker
{
    public class TableNameReaderFunction
    {
        private readonly SqlDataCheckerValidatorService _sqlDataCheckerValidator;
        private readonly ILogger<TableNameReaderFunction> _logger;

        public TableNameReaderFunction(SqlDataCheckerValidatorService sqlDataCheckerValidator, ILogger<TableNameReaderFunction> logger)
        {
            _sqlDataCheckerValidator = sqlDataCheckerValidator;
            _logger = logger;
        }

        [Function(nameof(TableNameReaderFunction))]
        public async Task<TableName> GetSqlDataCheckerTableName([ActivityTrigger] object obj)
        {
            _logger.FunctionStarted(nameof(TableNameReaderFunction));

            var tableNames = await _sqlDataCheckerValidator.GetTableNamesAsync();

            _logger.FunctionCompleted(nameof(TableNameReaderFunction));

            return tableNames;
        }
    }
}
