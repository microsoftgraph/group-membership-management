// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Data.SqlClient;
using Microsoft.Graph.Models;
using Models;
using Newtonsoft.Json;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Data;

namespace Services
{
    public class GetDefaultSqlMembershipSourceAttributesHandler : RequestHandlerBase<GetDefaultSqlMembershipSourceAttributesRequest, GetDefaultSqlMembershipSourceAttributesResponse>
    {
        private readonly ILoggingRepository _loggingRepository;
        private readonly IDatabaseSqlMembershipSourcesRepository _databaseSqlMembershipSourcesRepository;
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;

        private SemaphoreSlim _adfRunIdSemaphore = new SemaphoreSlim(1, 1);

        public GetDefaultSqlMembershipSourceAttributesHandler(ILoggingRepository loggingRepository,
                              IDatabaseSqlMembershipSourcesRepository databaseSqlMembershipSourcesRepository,
                              IDataFactoryRepository dataFactoryRepository,
                              ISqlMembershipRepository sqlMembershipRepository) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _databaseSqlMembershipSourcesRepository = databaseSqlMembershipSourcesRepository ?? throw new ArgumentNullException(nameof(databaseSqlMembershipSourcesRepository));
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
            _sqlMembershipRepository = sqlMembershipRepository;
        }

        protected override async Task<GetDefaultSqlMembershipSourceAttributesResponse> ExecuteCoreAsync(GetDefaultSqlMembershipSourceAttributesRequest request)
        {
            try
            {
                var sqlFilterAttributes = await GetSqlAttributesAsync();
                var storedAttributeSettings = await _databaseSqlMembershipSourcesRepository.GetDefaultSourceAttributesAsync();

                if (storedAttributeSettings != null)
                {
                    storedAttributeSettings.RemoveAll(attribute => !sqlFilterAttributes.Any(t => t.Name == attribute.Name && t.Type == attribute.Type));

                    await _databaseSqlMembershipSourcesRepository.UpdateDefaultSourceAttributesAsync(storedAttributeSettings);
                }

                var attributesToReturn = sqlFilterAttributes.Select(sqlAttribute =>
                {
                    var storedAttribute = storedAttributeSettings?.FirstOrDefault(attribute =>
                        attribute.Name == sqlAttribute.Name && attribute.Type == sqlAttribute.Type
                    );

                    return storedAttribute ?? sqlAttribute;

                }).ToList();

                return new GetDefaultSqlMembershipSourceAttributesResponse { Attributes = attributesToReturn };
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"Unable to retrieve Sql Filter Attributes: {ex.Message}" });
                throw ex;
            }
        }

        private async Task<List<SqlMembershipAttribute>> GetSqlAttributesAsync()
        {
            var tableName = await GetTableNameAsync();
            var columns = await GetColumnDetailsAsync(tableName);
            var attributes = columns.Select(column =>
            {
                var codeSuffix = "_Code";
                var attributeName = column.Name;
                var hasMapping = false;

                if (attributeName.EndsWith(codeSuffix))
                {
                    attributeName = attributeName.Substring(0, attributeName.Length - codeSuffix.Length);
                    hasMapping = true;
                }

                return new SqlMembershipAttribute
                {
                    Name = attributeName,
                    Type = column.Type,
                    CustomLabel = "",
                    HasMapping = hasMapping
                };
            }).ToList();

            return attributes;
        }

        private async Task<List<(string Name, string Type)>> GetColumnDetailsAsync(string tableName)
        {
            var attributes = new List<(string Name, string Type)>();

            try
            {
                attributes = await _sqlMembershipRepository.GetColumnDetailsAsync(tableName);
            }
            catch (SqlException ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"An exception was thrown while attempting to get the columns of Destination table '{tableName}': {ex.Message}" });
                throw ex;
            }

            return attributes;
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
                await _loggingRepository.LogMessageAsync(new LogMessage { Message = $"An exception was thrown while attempting to get the laterst ADF pipeline run: {message}" });
                throw new ArgumentException(message);
            }

            return lastSqlMembershipRunId;
        }
    }
}