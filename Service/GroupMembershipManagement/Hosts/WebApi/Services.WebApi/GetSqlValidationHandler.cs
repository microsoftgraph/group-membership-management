// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.WebApi;
using Microsoft.Data.SqlClient;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;
using Models.AdfRunHistory;

namespace Services
{
    public class GetSqlValidationHandler : RequestHandlerBase<GetSqlValidationRequest, GetSqlValidationResponse>
    {
        private readonly ILogger<GetSqlValidationHandler> _logger;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;
        private readonly IAdfRunHistoryRepository _adfRunHistoryRepository;

        public GetSqlValidationHandler(ILogger<GetSqlValidationHandler> logger,
                                ISqlMembershipRepository sqlMembershipRepository,
                                IAdfRunHistoryRepository adfRunHistoryRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
            _adfRunHistoryRepository = adfRunHistoryRepository ?? throw new ArgumentNullException(nameof(adfRunHistoryRepository));
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
                _logger.SqlFilterValidationFailed(ex);
                throw;
            }
        }

        private async Task<string> GetTableNameAsync()
        {
            var tableName = (await _adfRunHistoryRepository.GetLatestAdfRunAsync() ?? throw new InvalidOperationException(AdfRunHistory.NoEligibleRunMessage)).GetTableName();
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
                _logger.SqlTableExistsCheckFailed(tableName, ex);
                throw;
            }

            return tableExists;
        }
    }
}