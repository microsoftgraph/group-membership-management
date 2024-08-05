// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Data.SqlClient;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using SqlMembershipAttributeValueDTO = WebApi.Models.DTOs.SqlMembershipAttributeMapping;

namespace Services
{
    public class GetDefaultSqlMembershipSourceAttributeMappingsHandler : RequestHandlerBase<GetDefaultSqlMembershipSourceAttributeMappingsRequest, GetDefaultSqlMembershipSourceAttributeMappingsResponse>
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;

        public GetDefaultSqlMembershipSourceAttributeMappingsHandler(ILoggingRepository loggingRepository,
                              IDataFactoryRepository dataFactoryRepository,
                              ISqlMembershipRepository sqlMembershipRepository) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
        }

        protected override async Task<GetDefaultSqlMembershipSourceAttributeMappingsResponse> ExecuteCoreAsync(GetDefaultSqlMembershipSourceAttributeMappingsRequest request)
        {
            try
            {
                var response = new GetDefaultSqlMembershipSourceAttributeMappingsResponse();
                var attributeMappings = await GetSqlAttributeMappingsAsync(request.Attribute);
                foreach (var attributeValue in attributeMappings)
                {
                    var dto = new SqlMembershipAttributeValueDTO(attributeValue.Code, attributeValue.Description);

                    response.Model.Add(dto);
                }
                return response;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Unable to retrieve Sql Filter Attribute Mappings: {ex.Message}" });
                throw ex;
            }
        }

        private async Task<List<(string Code, string Description)>> GetSqlAttributeMappingsAsync(string attribute)
        {
            var tableName = await GetTableNameAsync();
            var attributes = await GetAttributeMappingsAsync(attribute, tableName);
            return attributes;
        }

        private async Task<List<(string Code, string Description)>> GetAttributeMappingsAsync(string attribute, string tableName)
        {
            var attributeMappings = new List<(string Code, string Description)>();

            try
            {
                attributeMappings = await _sqlMembershipRepository.GetAttributeMappingsAsync(attribute, tableName);
            }
            catch (SqlException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"An exception was thrown while attempting to get Sql Filter Attribute Mappings from mappings table '{tableName}': {ex.Message}" });
                throw ex;
            }

            return attributeMappings;
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
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"An exception was thrown while checking if table '{tableName}' exists: {ex.Message}" });
                throw ex;
            }

            return tableExists;
        }

        private async Task<string> GetADFRunIdAsync()
        {
            var lastSqlMembershipRunId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();

            if (string.IsNullOrWhiteSpace(lastSqlMembershipRunId))
            {
                var message = $"No SqlMembershipObtainer pipeline run has been found";
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"An exception was thrown while attempting to get the latest ADF pipeline run: {message}" });
                throw new ArgumentException(message);
            }

            return lastSqlMembershipRunId;
        }
    }
}