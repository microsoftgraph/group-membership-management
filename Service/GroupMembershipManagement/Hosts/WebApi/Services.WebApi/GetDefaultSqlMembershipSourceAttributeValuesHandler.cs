// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Data.SqlClient;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using SqlMembershipAttributeValueDTO = WebApi.Models.DTOs.SqlMembershipAttributeValue;

namespace Services
{
    public class GetDefaultSqlMembershipSourceAttributeValuesHandler : RequestHandlerBase<GetDefaultSqlMembershipSourceAttributeValuesRequest, GetDefaultSqlMembershipSourceAttributeValuesResponse>
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;

        private SemaphoreSlim _adfRunIdSemaphore = new SemaphoreSlim(1, 1);

        public GetDefaultSqlMembershipSourceAttributeValuesHandler(ILoggingRepository loggingRepository,
                              IDataFactoryRepository dataFactoryRepository,
                              ISqlMembershipRepository sqlMembershipRepository) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
        }

        protected override async Task<GetDefaultSqlMembershipSourceAttributeValuesResponse> ExecuteCoreAsync(GetDefaultSqlMembershipSourceAttributeValuesRequest request)
        {
            try
            {
                var response = new GetDefaultSqlMembershipSourceAttributeValuesResponse();
                var attributeValues = await GetSqlAttributeValues(request.Attribute);
                foreach (var attributeValue in attributeValues)
                {
                    var dto = new SqlMembershipAttributeValueDTO(attributeValue.Code, attributeValue.Description);

                    response.Model.Add(dto);
                }
                return response;
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Unable to retrieve Sql Filter Attribute Values: {ex.Message}" });
                throw ex;
            }
        }

        private async Task<List<(string Code, string Description)>> GetSqlAttributeValues(string attribute)
        {
            var tableName = await GetTableNameAsync();
            var attributes = await GetAttributeValuesAsync(attribute, tableName);
            return attributes;
        }

        private async Task<List<(string Code, string Description)>> GetAttributeValuesAsync(string attribute, string tableName)
        {
            var attributeValues = new List<(string Code, string Description)>();

            try
            {
                attributeValues = await _sqlMembershipRepository.GetAttributeValuesAsync(attribute, tableName);
            }
            catch (SqlException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"An exception was thrown while attempting to get Sql Filter Attribute Values from mappings table '{tableName}': {ex.Message}" });
                throw ex;
            }

            return attributeValues;
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
            await _adfRunIdSemaphore.WaitAsync();

            var lastSqlMembershipRunId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();

            _adfRunIdSemaphore.Release();

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