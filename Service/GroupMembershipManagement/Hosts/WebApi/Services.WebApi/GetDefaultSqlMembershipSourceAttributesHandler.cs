// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Hosts.WebApi;
using Microsoft.Data.SqlClient;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System.Data;
using Microsoft.Extensions.Logging;

namespace Services
{
    public class GetDefaultSqlMembershipSourceAttributesHandler : RequestHandlerBase<GetDefaultSqlMembershipSourceAttributesRequest, GetDefaultSqlMembershipSourceAttributesResponse>
    {
        private readonly ILogger<GetDefaultSqlMembershipSourceAttributesHandler> _logger;
        private readonly IDatabaseSqlMembershipSourcesRepository _databaseSqlMembershipSourcesRepository;
        private readonly IDataFactoryRepository _dataFactoryRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;

        private SemaphoreSlim _adfRunIdSemaphore = new SemaphoreSlim(1, 1);

        public GetDefaultSqlMembershipSourceAttributesHandler(ILogger<GetDefaultSqlMembershipSourceAttributesHandler> logger,
                              IDatabaseSqlMembershipSourcesRepository databaseSqlMembershipSourcesRepository,
                              IDataFactoryRepository dataFactoryRepository,
                              ISqlMembershipRepository sqlMembershipRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                    storedAttributeSettings.RemoveAll(attribute => !sqlFilterAttributes.Any(t => t.Name == attribute.Name));

                    await _databaseSqlMembershipSourcesRepository.UpdateDefaultSourceAttributesAsync(storedAttributeSettings);
                }

                var attributesToReturn = sqlFilterAttributes.Select(sqlAttribute =>
                {
                    var storedAttribute = storedAttributeSettings?.FirstOrDefault(attribute =>
                        attribute.Name == sqlAttribute.Name
                    );

                    sqlAttribute.CustomLabel = storedAttribute?.CustomLabel ?? "";
                    sqlAttribute.Description = storedAttribute?.Description ?? "";
                    sqlAttribute.Enabled = storedAttribute?.Enabled ?? true;
                    return sqlAttribute;

                }).ToList();

                return new GetDefaultSqlMembershipSourceAttributesResponse { Attributes = attributesToReturn };
            }
            catch (Exception ex)
            {
                _logger.SqlFilterAttributesRetrievalFailed(ex);
                throw ex;
            }
        }

        private async Task<List<SqlMembershipAttribute>> GetSqlAttributesAsync()
        {
            var tableName = await GetTableNameAsync();
            var columns = await GetColumnDetailsAsync(tableName);

            if (columns.Count == 0)
            {
                _logger.SqlMembershipAttributesTableMissing(tableName);
                throw new InvalidOperationException($"Unable to retrieve SQL membership attributes. The ADF HR data table '{tableName}' does not exist or has no columns.");
            }

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
                    Description = "",
                    Enabled = true,
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
                _logger.SqlColumnDetailsRetrievalFailed(tableName, ex);
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
                _logger.SqlTableExistsCheckFailed(tableName, ex);
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
                _logger.SqlMembershipAdfRunIdNotFound();
                throw new ArgumentException("No SqlMembershipObtainer pipeline run has been found");
            }

            return lastSqlMembershipRunId;
        }
    }
}