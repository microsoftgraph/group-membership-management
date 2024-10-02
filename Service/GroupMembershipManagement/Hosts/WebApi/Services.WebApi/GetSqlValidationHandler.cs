// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Data.SqlClient;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;

namespace Services
{
    public class GetSqlValidationHandler : RequestHandlerBase<GetSqlValidationRequest, GetSqlValidationResponse>
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;
        private readonly IDataFactoryRepository _dataFactoryRepository;

        private SemaphoreSlim _adfRunIdSemaphore = new SemaphoreSlim(1, 1);

        public GetSqlValidationHandler(ILoggingRepository loggingRepository,
                                ISqlMembershipRepository sqlMembershipRepository,
                                IDataFactoryRepository dataFactoryRepository) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
        }

        protected override async Task<GetSqlValidationResponse> ExecuteCoreAsync(GetSqlValidationRequest request)
        {
            if (request.SqlFilters == null || request.SqlFilters.Count == 0)
            {
                return new GetSqlValidationResponse
                {
                    IsValid = true,
                    Errors = null
                };
            }

            try
            {
                var tableName = await GetTableNameAsync();
                var sqlExceptions = await _sqlMembershipRepository.ValidateFiltersAsync(request.SqlFilters, tableName);

                return new GetSqlValidationResponse
                {
                    IsValid = sqlExceptions.Count == 0,
                    Errors = sqlExceptions.Count == 0 ? null : sqlExceptions
                };
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Unable to validate Sql filter: {ex.Message}" });
                throw;
            }
        }

        private async Task<string> GetTableNameAsync()
        {
            var adfRunId = await GetADFRunIdAsync();
            var tableName = adfRunId.Replace("-", "");
            var tableExists = await CheckIfTableExistsAsync(tableName);

            return tableExists ? tableName : "";
        }

        private async Task<bool> CheckIfTableExistsAsync(string tableName)
        {
            bool tableExists = false;

            try
            {
                tableExists = await _sqlMembershipRepository.CheckIfTableExistsAsync(tableName);
            }
            catch (SqlException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"An exception was thrown while checking if table '{tableName}' exists: {ex.Message}" });
                throw;
            }

            return tableExists;
        }

        private async Task<string> GetADFRunIdAsync()
        {
            await _adfRunIdSemaphore.WaitAsync();

            var lastSqlMembershipRunId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();

            _adfRunIdSemaphore.Release();

            if (string.IsNullOrWhiteSpace(lastSqlMembershipRunId))
            {
                var message = $"No SqlMembershipObtainer pipeline run has been found";
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"An exception was thrown while attempting to get the laterst ADF pipeline run: {message}" });
                throw new ArgumentException(message);
            }

            return lastSqlMembershipRunId;
        }
    }
}