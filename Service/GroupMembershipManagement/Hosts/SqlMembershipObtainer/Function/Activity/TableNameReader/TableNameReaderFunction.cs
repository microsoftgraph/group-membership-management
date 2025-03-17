// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
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

        [FunctionName(nameof(TableNameReaderFunction))]
        public async Task<string> GetSqlMembershipTableName([ActivityTrigger] TableNameReaderRequest request)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TableNameReaderFunction)} function started", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            string sqlMembershipObtainerTableName = null;

            await _retryPolicy.ExecuteAsync(async () =>
            {
                sqlMembershipObtainerTableName = await _sqlMembershipObtainerService.GetTableNameAsync(request.SyncJob.RunId, request.GroupId);
            });

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TableNameReaderFunction)} function completed", RunId = request.SyncJob.RunId }, VerbosityLevel.DEBUG);

            return sqlMembershipObtainerTableName;
        }

        private readonly AsyncRetryPolicy _retryPolicy = Policy
            .Handle<SqlException>(ex => ex.Number == -2) // SQL timeout exception number
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }
}
