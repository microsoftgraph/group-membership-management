// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.SqlMembershipObtainer;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Repositories.Contracts.Helpers;
using Services.Contracts;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Polly.Retry;
using Polly;

namespace SqlMembershipObtainer
{
    public class TableNameReaderFunction
    {
        private readonly ILogger<TableNameReaderFunction> _logger;
        private readonly ISqlMembershipObtainerService _sqlMembershipObtainerService;

        public TableNameReaderFunction(ILogger<TableNameReaderFunction> logger, ISqlMembershipObtainerService sqlMembershipObtainerService)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sqlMembershipObtainerService = sqlMembershipObtainerService ?? throw new ArgumentNullException(nameof(sqlMembershipObtainerService));
        }

        [Function(nameof(TableNameReaderFunction))]
        public async Task<string> GetSqlMembershipTableName([ActivityTrigger] TableNameReaderRequest request)
        {
            using (_logger.BeginSyncJobScope(request.SyncJob, new Dictionary<string, object>
            {
                ["CurrentPart"] = request.CurrentPart,
                ["TotalParts"] = request.TotalParts
            }))
            {
                _logger.FunctionStarted(nameof(TableNameReaderFunction));

                string sqlMembershipObtainerTableName = null;

                await _retryPolicy.ExecuteAsync(async () =>
                {
                    sqlMembershipObtainerTableName = await _sqlMembershipObtainerService.GetTableNameAsync(request.SyncJob.RunId, request.GroupId);
                });

                _logger.FunctionCompleted(nameof(TableNameReaderFunction));

                return sqlMembershipObtainerTableName;
            }
        }

        private readonly AsyncRetryPolicy _retryPolicy = Policy
            .Handle<SqlException>(ex => ex.Number == -2) // SQL timeout exception number
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }
}