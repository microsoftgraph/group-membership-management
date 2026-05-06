// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.Data.SqlClient;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class GetDefaultSqlMembershipSourceAttributeValuesHandler : RequestHandlerBase<GetDefaultSqlMembershipSourceAttributeValuesRequest, GetDefaultSqlMembershipSourceAttributeValuesResponse>
    {
        private readonly ILogger<GetDefaultSqlMembershipSourceAttributeValuesHandler> _logger;
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;

        public GetDefaultSqlMembershipSourceAttributeValuesHandler(ILogger<GetDefaultSqlMembershipSourceAttributeValuesHandler> logger,
                              IDataFactoryRepository dataFactoryRepository,
                              ISqlMembershipRepository sqlMembershipRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
        }

        protected override async Task<GetDefaultSqlMembershipSourceAttributeValuesResponse> ExecuteCoreAsync(GetDefaultSqlMembershipSourceAttributeValuesRequest request)
        {
            try
            {
                var response = new GetDefaultSqlMembershipSourceAttributeValuesResponse();
                var attributeValues = await GetSqlAttributeValuesAsync(request.Attribute, request.HasMapping);
                response.Values = attributeValues;

                return response;
            }
            catch (Exception ex)
            {
                _logger.SqlFilterAttributeValuesRetrievalFailed(ex);
                throw ex;
            }
        }

        private async Task<List<string>> GetSqlAttributeValuesAsync(string attribute, bool hasMapping)
        {
            var tableName = await GetTableNameAsync();
            var attributes = await GetAttributeValuesAsync(attribute, hasMapping, tableName);
            return attributes;
        }

        private async Task<List<string>> GetAttributeValuesAsync(string attribute, bool hasMapping, string tableName)
        {
            var attributeValues = new List<string>();

            try
            {
                attributeValues = await _sqlMembershipRepository.GetAttributeValuesAsync(attribute, hasMapping, tableName);
            }
            catch (SqlException ex)
            {
                _logger.SqlAttributeValuesRetrievalFailed(tableName, ex);
                throw ex;
            }

            return attributeValues;
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
                _logger.SqlTableExistsCheckFailed(tableName, ex);
                throw ex;
            }

            return tableExists;
        }

        private async Task<string> GetADFRunIdAsync()
        {
            var lastSqlMembershipRunId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();

            if (string.IsNullOrWhiteSpace(lastSqlMembershipRunId))
            {
                _logger.SqlMembershipAdfRunIdNotFound();
                throw new ArgumentException("No SqlMembershipObtainer pipeline run has been found");
            }

            return lastSqlMembershipRunId;
        }
    }
}