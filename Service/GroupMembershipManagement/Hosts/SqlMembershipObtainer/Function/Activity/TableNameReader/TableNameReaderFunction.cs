// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Azure.Functions.Worker;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Polly.Retry;
using Polly;

namespace SqlMembershipObtainer
{
    public class TableNameReaderFunction
    {
        private readonly ISqlMembershipObtainerService _sqlMembershipObtainerService = null;
        private readonly ILoggingRepository _loggingRepository = null;

        public TableNameReaderFunction(ISqlMembershipObtainerService sqlMembershipObtainerService, ILoggingRepository loggingRepository)
        {
            _sqlMembershipObtainerService = sqlMembershipObtainerService ?? throw new ArgumentNullException(nameof(sqlMembershipObtainerService));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
        }

        [Function(nameof(TableNameReaderFunction))]
        public async Task<string> GetSqlMembershipTableName([ActivityTrigger] SyncJob syncJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TableNameReaderFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);

            string sqlMembershipObtainerTableName = null;

            await _retryPolicy.ExecuteAsync(async () =>
            {
                sqlMembershipObtainerTableName = await _sqlMembershipObtainerService.GetTableNameAsync(syncJob.RunId, syncJob.TargetOfficeGroupId);
            });

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TableNameReaderFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);

            return sqlMembershipObtainerTableName;
        }

        private readonly AsyncRetryPolicy _retryPolicy = Policy
            .Handle<SqlException>(ex => ex.Number == -2) // SQL timeout exception number
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }
}
