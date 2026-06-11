// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.SqlDataChecker;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SqlDataChecker
{
    public class ColumnReaderFunction
    {
        private readonly SqlDataCheckerValidatorService _sqlDataCheckerValidator;
        private readonly ILogger<ColumnReaderFunction> _logger;

        public ColumnReaderFunction(SqlDataCheckerValidatorService sqlDataCheckerValidator, ILogger<ColumnReaderFunction> logger)
        {
            _sqlDataCheckerValidator = sqlDataCheckerValidator ?? throw new System.ArgumentNullException(nameof(sqlDataCheckerValidator));
            _logger = logger ?? throw new System.ArgumentNullException(nameof(logger));
        }

        [Function(nameof(ColumnReaderFunction))]
        public Task<List<string>> GetColumns([ActivityTrigger] string tableName)
        {
            _logger.FunctionStarted(nameof(ColumnReaderFunction));

            var columns = _sqlDataCheckerValidator.GetColumns(tableName);

            _logger.FunctionCompleted(nameof(ColumnReaderFunction));

            return Task.FromResult(columns);
        }
    }
}
