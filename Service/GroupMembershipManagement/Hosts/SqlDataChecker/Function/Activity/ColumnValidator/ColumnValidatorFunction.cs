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
    public class ColumnValidatorFunction
    {
        private readonly SqlDataCheckerValidatorService _sqlDataCheckerValidator;
        private readonly ILogger<ColumnValidatorFunction> _logger;

        public ColumnValidatorFunction(SqlDataCheckerValidatorService sqlDataCheckerValidator, ILogger<ColumnValidatorFunction> logger)
        {
            _sqlDataCheckerValidator = sqlDataCheckerValidator;
            _logger = logger;
        }

        [Function(nameof(ColumnValidatorFunction))]
        public Task<Dictionary<string, int>> ValidateColumn([ActivityTrigger] ColumnValidatorRequest request)
        {
            _logger.FunctionStarted(nameof(ColumnValidatorFunction));
            var columns = new Dictionary<string, int>();
            if (request.Columns != null && request.Table != null)
            {
                columns = _sqlDataCheckerValidator.ValidateColumns(request.Columns, request.Table);
            }
            _logger.FunctionCompleted(nameof(ColumnValidatorFunction));
            return Task.FromResult(columns);
        }
    }
}
