// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.SqlDataChecker;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Services;
using System.Threading.Tasks;

namespace SqlDataChecker
{
    public class RowReaderFunction
    {
        private readonly SqlDataCheckerValidatorService _sqlDataCheckerValidator;
        private readonly ILogger<RowReaderFunction> _logger;

        public RowReaderFunction(SqlDataCheckerValidatorService sqlDataCheckerValidator, ILogger<RowReaderFunction> logger)
        {
            _sqlDataCheckerValidator = sqlDataCheckerValidator;
            _logger = logger;
        }

        [Function(nameof(RowReaderFunction))]
        public Task<int> GetRows([ActivityTrigger] string tableName)
        {
            _logger.FunctionStarted(nameof(RowReaderFunction));

            var numberOfRows = _sqlDataCheckerValidator.GetRows(tableName);

            _logger.FunctionCompleted(nameof(RowReaderFunction));

            return Task.FromResult(numberOfRows);
        }
    }
}
