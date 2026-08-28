// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.WebApi;
using Microsoft.Data.SqlClient;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Microsoft.Extensions.Logging;
using SqlMembershipAttributeValueDTO = WebApi.Models.DTOs.SqlMembershipAttributeMapping;

namespace Services
{
    /// <summary>
    /// Resolves an explicit set of attribute codes to their descriptions. The browse endpoint returns only a
    /// capped page, so values already saved on a sync job are resolved here to guarantee they always render
    /// with their real description rather than a bare code.
    /// </summary>
    public class ResolveDefaultSqlMembershipSourceAttributeMappingsHandler : RequestHandlerBase<ResolveDefaultSqlMembershipSourceAttributeMappingsRequest, ResolveDefaultSqlMembershipSourceAttributeMappingsResponse>
    {
        private readonly ILogger<ResolveDefaultSqlMembershipSourceAttributeMappingsHandler> _logger;
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;

        public ResolveDefaultSqlMembershipSourceAttributeMappingsHandler(ILogger<ResolveDefaultSqlMembershipSourceAttributeMappingsHandler> logger,
                              IDataFactoryRepository dataFactoryRepository,
                              ISqlMembershipRepository sqlMembershipRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
        }

        protected override async Task<ResolveDefaultSqlMembershipSourceAttributeMappingsResponse> ExecuteCoreAsync(ResolveDefaultSqlMembershipSourceAttributeMappingsRequest request)
        {
            var response = new ResolveDefaultSqlMembershipSourceAttributeMappingsResponse();

            if (request.Codes == null || request.Codes.Count == 0)
            {
                return response;
            }

            try
            {
                var tableName = await GetTableNameAsync();
                var attributeMappings = await GetAttributeMappingsByCodesAsync(request.Attribute, tableName, request.Codes);

                foreach (var attributeMapping in attributeMappings)
                {
                    response.Mappings.Add(new SqlMembershipAttributeValueDTO(attributeMapping.Code, attributeMapping.Description));
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.SqlFilterAttributeMappingsRetrievalFailed(ex);
                throw;
            }
        }

        private async Task<List<(string Code, string Description)>> GetAttributeMappingsByCodesAsync(string attribute, string tableName, IReadOnlyList<string> codes)
        {
            try
            {
                return await _sqlMembershipRepository.GetAttributeMappingsByCodesAsync(attribute, tableName, codes);
            }
            catch (SqlException ex)
            {
                _logger.SqlAttributeMappingsRetrievalFailed(tableName, ex);
                throw;
            }
        }

        private async Task<string> GetTableNameAsync()
        {
            var adfRunId = await GetADFRunIdAsync();
            var tableName = adfRunId.Replace("-", "");
            var tableExists = await CheckIfMappingsTableExistsAsync(tableName);

            return tableExists ? tableName : "";
        }

        private async Task<bool> CheckIfMappingsTableExistsAsync(string tableName)
        {
            bool tableExists = false;

            try
            {
                tableExists = await _sqlMembershipRepository.CheckIfMappingsTableExistsAsync(tableName);
            }
            catch (SqlException ex)
            {
                _logger.SqlMappingsTableExistsCheckFailed(tableName, ex);
                throw;
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
