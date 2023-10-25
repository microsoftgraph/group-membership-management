// Copyright(c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Repositories.Contracts;
using Services.Contracts;
using System;
using System.Threading.Tasks;

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
        public async Task<string> GetSqlMembershipTableName([ActivityTrigger] SyncJob syncJob)
        {
            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TableNameReaderFunction)} function started", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);

            var sqlMembershipObtainerTableName = await _sqlMembershipObtainerService.GetTableNameAsync(syncJob.RunId, syncJob.TargetOfficeGroupId);

            await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"{nameof(TableNameReaderFunction)} function completed", RunId = syncJob.RunId }, VerbosityLevel.DEBUG);

            return sqlMembershipObtainerTableName;
        }
    }
}
