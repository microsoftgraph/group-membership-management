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
    public class ColumnValidatorFunction
    {
        private readonly SqlDataCheckerValidatorService _sqlDataCheckerValidator = null;
        private readonly ILoggingRepository _loggingRepository = null;

        public ColumnValidatorFunction(SqlDataCheckerValidatorService sqlDataCheckerValidator, ILoggingRepository loggingRepository)
        {
            _sqlDataCheckerValidator = sqlDataCheckerValidator ?? throw new ArgumentNullException(nameof(sqlDataCheckerValidator));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(ColumnValidatorFunction))]
        public async Task<Dictionary<string, int>> ValidateColumn([ActivityTrigger] ColumnValidatorRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ColumnValidatorFunction)} function started" }, VerbosityLevel.DEBUG);
            var columns = new Dictionary<string, int>();
            if (request.Columns != null && request.Table != null)
            {
                columns = _sqlDataCheckerValidator.ValidateColumns(request.Columns, request.Table);
            }
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(ColumnValidatorFunction)} function completed" }, VerbosityLevel.DEBUG);
            return columns;
        }
    }
}
