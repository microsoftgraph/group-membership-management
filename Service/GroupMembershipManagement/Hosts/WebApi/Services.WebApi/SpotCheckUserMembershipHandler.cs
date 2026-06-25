// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Extensions.Logging;
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using System;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using WebApi.Models.DTOs;

namespace Services
{
    public class SpotCheckUserMembershipHandler : RequestHandlerBase<SpotCheckUserMembershipRequest, SpotCheckUserMembershipResponse>
    {
        private const string GroupMembershipType = "GroupMembership";
        private const string SqlMembershipType = "SqlMembership";

        private readonly ILogger<SpotCheckUserMembershipHandler> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;
        private readonly IDataFactoryRepository _dataFactoryRepository;

        public SpotCheckUserMembershipHandler(
            ILogger<SpotCheckUserMembershipHandler> logger,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            IGraphGroupRepository graphGroupRepository,
            ISqlMembershipRepository sqlMembershipRepository,
            IDataFactoryRepository dataFactoryRepository) : base(logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
            _dataFactoryRepository = dataFactoryRepository ?? throw new ArgumentNullException(nameof(dataFactoryRepository));
        }

        protected override async Task<SpotCheckUserMembershipResponse> ExecuteCoreAsync(SpotCheckUserMembershipRequest request)
        {
            var response = new SpotCheckUserMembershipResponse { StatusCode = HttpStatusCode.OK };

            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                response.StatusCode = HttpStatusCode.BadRequest;
                return response;
            }

            var job = await _databaseSyncJobsRepository.GetSyncJobAsync(request.SyncJobId);
            if (job == null)
            {
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            var result = new SpotCheckResult();

            var accountEnabled = await _graphGroupRepository.GetUserAccountEnabledAsync(request.UserId);
            if (accountEnabled == null)
            {
                // The user could not be found in Entra ID.
                response.StatusCode = HttpStatusCode.NotFound;
                return response;
            }

            result.AccountEnabled = accountEnabled.Value;

            if (!accountEnabled.Value)
            {
                // Account is disabled - do not evaluate source parts.
                response.Model = result;
                return response;
            }

            if (string.IsNullOrWhiteSpace(job.Query))
            {
                response.Model = result;
                return response;
            }

            JsonElement[] parts;
            try
            {
                using var document = JsonDocument.Parse(job.Query);
                if (document.RootElement.ValueKind != JsonValueKind.Array)
                {
                    response.Model = result;
                    return response;
                }
                parts = document.RootElement.EnumerateArray().Select(e => e.Clone()).ToArray();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse sync job query for spot-check. SyncJobId: {SyncJobId}", request.SyncJobId);
                response.StatusCode = HttpStatusCode.InternalServerError;
                return response;
            }

            // Resolve the SQL membership table name lazily; only needed when a SqlMembership part is present.
            var sqlTableNameResolved = false;
            string sqlTableName = null;

            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                var type = part.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null;
                var exclusionary = part.TryGetProperty("exclusionary", out var exProp)
                    && (exProp.ValueKind == JsonValueKind.True || (exProp.ValueKind == JsonValueKind.String && bool.TryParse(exProp.GetString(), out var ex) && ex));

                var partResult = new SpotCheckPartResult
                {
                    Index = index,
                    Type = type,
                    Exclusionary = exclusionary,
                    Supported = false,
                    Included = null
                };

                if (string.Equals(type, GroupMembershipType, StringComparison.OrdinalIgnoreCase))
                {
                    partResult.Supported = true;
                    partResult.Included = await EvaluateGroupMembershipAsync(part, request.UserId);
                }
                else if (string.Equals(type, SqlMembershipType, StringComparison.OrdinalIgnoreCase))
                {
                    partResult.Supported = true;

                    if (!sqlTableNameResolved)
                    {
                        sqlTableName = await ResolveSqlTableNameAsync();
                        sqlTableNameResolved = true;
                    }

                    partResult.Included = await EvaluateSqlMembershipAsync(part, request.UserId, sqlTableName);
                }
                else
                {
                    result.HasUnsupportedParts = true;
                }

                result.Parts.Add(partResult);
            }

            response.Model = result;
            return response;
        }

        private async Task<bool?> EvaluateGroupMembershipAsync(JsonElement part, string userId)
        {
            if (!part.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            if (!Guid.TryParse(source.GetString(), out var groupId))
            {
                return null;
            }

            try
            {
                return await _graphGroupRepository.IsEmailRecipientMemberOfGroupAsync(userId, groupId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate group membership for spot-check. GroupId: {GroupId}", groupId);
                return null;
            }
        }

        private async Task<bool?> EvaluateSqlMembershipAsync(JsonElement part, string userId, string tableName)
        {
            if (string.IsNullOrWhiteSpace(tableName))
            {
                // The HR data table is unavailable; membership cannot be determined.
                return null;
            }

            if (!part.TryGetProperty("source", out var source) || source.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string filter = null;
            if (source.TryGetProperty("filter", out var filterProp) && filterProp.ValueKind == JsonValueKind.String)
            {
                filter = filterProp.GetString();
            }

            var managerId = 0;
            var depth = 0;
            if (source.TryGetProperty("manager", out var managerProp) && managerProp.ValueKind == JsonValueKind.Object)
            {
                if (managerProp.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number && idProp.TryGetInt32(out var idValue))
                {
                    managerId = idValue;
                }
                if (managerProp.TryGetProperty("depth", out var depthProp) && depthProp.ValueKind == JsonValueKind.Number && depthProp.TryGetInt32(out var depthValue))
                {
                    depth = depthValue;
                }
            }

            try
            {
                if (managerId > 0)
                {
                    // Org-structure part: build the manager's reporting tree (optionally filtered) and check membership.
                    var children = await _sqlMembershipRepository.GetChildEntitiesAsync(filter, managerId, tableName, depth);
                    return children.Any(c => string.Equals(c.AzureObjectId, userId, StringComparison.OrdinalIgnoreCase));
                }

                if (!string.IsNullOrWhiteSpace(filter))
                {
                    return await _sqlMembershipRepository.IsUserInFilterAsync(filter, tableName, userId);
                }

                // No manager and no filter - the part resolves to an empty set.
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate SQL membership for spot-check.");
                return null;
            }
        }

        private async Task<string> ResolveSqlTableNameAsync()
        {
            try
            {
                var adfRunId = await _dataFactoryRepository.GetMostRecentSucceededRunIdAsync();
                if (string.IsNullOrWhiteSpace(adfRunId))
                {
                    return null;
                }

                var tableName = adfRunId.Replace("-", "");
                return await _sqlMembershipRepository.CheckIfTableExistsAsync(tableName) ? tableName : null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve SQL membership table name for spot-check.");
                return null;
            }
        }
    }
}
