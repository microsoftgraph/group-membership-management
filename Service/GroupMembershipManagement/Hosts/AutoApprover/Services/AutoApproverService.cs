// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.AutoApprover;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;
using Models;
using Models.ServiceBus;
using Models.SyncJobChange;
using Repositories.Contracts;
using Repositories.Contracts.Helpers;
using Repositories.Contracts.Helpers;
using Services.AutoApprover.Contracts;
using Services.AutoApprover.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Services.AutoApprover
{
    public class AutoApproverService : IAutoApproverService
    {
        private readonly IDatabaseSyncJobsRepository _syncJobRepository;
        private readonly IDatabaseSettingsRepository _databaseSettingsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ISyncJobChangeRepository _syncJobChangeRepository;
        private readonly ISqlMembershipRepository _sqlMembershipRepository;
        private readonly IAdfRunHistoryRepository _adfRunHistoryRepository;
        private readonly IDatabaseSqlMembershipSourcesRepository _sqlMembershipSourcesRepository;
        private readonly ILogger<AutoApproverService> _logger;
        private readonly TelemetryClient _telemetryClient;

        // A submitted filter is caller-supplied text, so bound the match.
        private static readonly TimeSpan SensitiveMatchTimeout = TimeSpan.FromSeconds(2);

        public AutoApproverService(
            IDatabaseSyncJobsRepository syncJobRepository,
            IDatabaseSettingsRepository databaseSettingsRepository,
            IGraphGroupRepository graphGroupRepository,
            ISyncJobChangeRepository syncJobChangeRepository,
            ISqlMembershipRepository sqlMembershipRepository,
            IAdfRunHistoryRepository adfRunHistoryRepository,
            IDatabaseSqlMembershipSourcesRepository sqlMembershipSourcesRepository,
            ILogger<AutoApproverService> logger,
            TelemetryClient telemetryClient)
        {
            _syncJobRepository = syncJobRepository ?? throw new ArgumentNullException(nameof(syncJobRepository));
            _databaseSettingsRepository = databaseSettingsRepository ?? throw new ArgumentNullException(nameof(databaseSettingsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _syncJobChangeRepository = syncJobChangeRepository ?? throw new ArgumentNullException(nameof(syncJobChangeRepository));
            _sqlMembershipRepository = sqlMembershipRepository ?? throw new ArgumentNullException(nameof(sqlMembershipRepository));
            _adfRunHistoryRepository = adfRunHistoryRepository ?? throw new ArgumentNullException(nameof(adfRunHistoryRepository));
            _sqlMembershipSourcesRepository = sqlMembershipSourcesRepository ?? throw new ArgumentNullException(nameof(sqlMembershipSourcesRepository));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _telemetryClient = telemetryClient ?? throw new ArgumentNullException(nameof(telemetryClient));
        }

        public async Task ProcessAutoApprovalAsync(AutoApprovalQueueMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));

            var syncJob = await _syncJobRepository.GetSyncJobAsync(message.SyncJobId);
            if (syncJob == null)
            {
                _logger.SyncJobNotFound(message.SyncJobId);
                return;
            }

            // Every log emitted below carries the job, destination group and requestor, so a single
            // `where customDimensions.Id == '...'` returns the whole decision trail.
            using var logScope = _logger.BeginSyncJobScope(syncJob, new Dictionary<string, object>
            {
                ["Requestor"] = Convert.ToString(syncJob.Requestor)
            });

            if (syncJob.Status == SyncStatus.PendingConfiguration.ToString())
            {
                _logger.SyncJobPendingConfiguration(message.SyncJobId);
                return;
            }

            if (!string.Equals(syncJob.Status, SyncStatus.PendingAutoApproval.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                _logger.SyncJobStatusNotPendingReview(message.SyncJobId, syncJob.Status);
                return;
            }

            if (!Guid.TryParse(message.RequestorObjectId, out var requestorObjectId))
            {
                _logger.InvalidRequestorObjectId(message.SyncJobId);
                return;
            }

            // WebApi has already verified that a config-only update leaves the query unchanged.
            var isConfigurationApproval = message.Origin == AutoApprovalOrigin.ConfigUpdate
                && message.ConfigurationQuery != null
                && string.Equals(message.ConfigurationQuery, syncJob.Query, StringComparison.Ordinal);
            if (message.Origin == AutoApprovalOrigin.ConfigUpdate && !isConfigurationApproval)
            {
                // A newer submission owns the current query; never evaluate it as the older requestor.
                _logger.StaleConfigurationUpdateIgnored(message.SyncJobId);
                return;
            }
            var shouldAutoApprove = isConfigurationApproval;
            if (!shouldAutoApprove)
            {
                var isGroupBasedAutoApprovalEnabled = await IsAutoApprovalForGroupBasedSyncsEnabledAsync();
                var isOrgLeaderAutoApprovalEnabled = await IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabledAsync();
                _logger.AutoApprovalModesResolved(isGroupBasedAutoApprovalEnabled, isOrgLeaderAutoApprovalEnabled);
                shouldAutoApprove = await ShouldAutoApproveJobAsync(syncJob.Query, message.RequestorObjectId, isGroupBasedAutoApprovalEnabled, isOrgLeaderAutoApprovalEnabled);
            }

            if (!shouldAutoApprove)
            {
                _logger.AutoApprovalNotGranted(message.SyncJobId);
                syncJob.Status = SyncStatus.PendingReview.ToString();
                await _syncJobRepository.UpdateSyncJobsAsync(new[] { syncJob });
                return;
            }

            if (syncJob.StartDate < DateTime.UtcNow && message.Origin == AutoApprovalOrigin.Onboarding)
            {
                // The onboarding path pushes StartDate out 24h so a freshly onboarded job does not run immediately.
                // An update-originated approval must never expedite or reschedule an existing job, so the +24h push
                // is skipped for both update origins (FR-017).
                syncJob.StartDate = DateTime.UtcNow.AddHours(24);
            }

            syncJob.Status = SyncStatus.Idle.ToString();

            var changeReason = message.Origin is AutoApprovalOrigin.Update or AutoApprovalOrigin.ConfigUpdate
                ? SyncJobChangeReason.UpdateAutoApproved
                : SyncJobChangeReason.OnboardingAutoApproved;

            var changedOnBehalfOfDisplayName = message.ChangedOnBehalfOfDisplayName;
            var changedOnBehalfOfObjectId = message.ChangedOnBehalfOfObjectId;

            var changeRecord = new SyncJobChange
            {
                SyncJobId = message.SyncJobId,
                ChangeTime = DateTime.UtcNow,
                ChangedByObjectId = requestorObjectId,
                ChangedByDisplayName = message.RequestorDisplayName,
                ChangeSource = SyncJobChangeSource.WebApp,
                ChangeReason = changeReason.ToString(),
                ChangeDetails = SyncJobSerializationHelper.SerializeSyncJob(syncJob),
                BusinessJustification = message.BusinessJustification,
                ChangedOnBehalfOfDisplayName = changedOnBehalfOfDisplayName != null && changedOnBehalfOfDisplayName != message.RequestorDisplayName ? changedOnBehalfOfDisplayName : null,
                ChangedOnBehalfOfObjectId = !string.IsNullOrEmpty(changedOnBehalfOfObjectId) && changedOnBehalfOfObjectId != message.RequestorObjectId ? new Guid(changedOnBehalfOfObjectId) : (Guid?)null
            };

            try
            {
                await _syncJobRepository.UpdateSyncJobsAsync(new[] { syncJob });
                await _syncJobChangeRepository.Save(changeRecord);
            }
            catch (Exception ex)
            {
                // The status update and the audit record are not written atomically. If the status was
                // already flipped to Idle but persisting the audit record failed, revert the job back to
                // PendingAutoApproval so the Service Bus retry re-runs the full approval path (and writes the
                // missing audit record) instead of short-circuiting on the "not PendingAutoApproval" guard.
                _logger.AutoApprovalPersistenceFailed(message.SyncJobId, ex);

                try
                {
                    syncJob.Status = SyncStatus.PendingAutoApproval.ToString();
                    await _syncJobRepository.UpdateSyncJobsAsync(new[] { syncJob });
                }
                catch (Exception revertEx)
                {
                    _logger.AutoApprovalRevertFailed(message.SyncJobId, revertEx);
                }

                throw;
            }

            _logger.SyncJobAutoApproved(message.SyncJobId);
            if (isConfigurationApproval)
            {
                _logger.ConfigurationUpdateAutoApproved(message.SyncJobId);
            }

            // Emitted only once the approval and its audit row are durably persisted, so the dashboard
            // count reconciles one-to-one with the OnboardingAutoApproved / UpdateAutoApproved rows in
            // SyncJobChanges. The request type comes from the same message origin that chose the audit
            // reason above, so the two can never disagree about whether this was an onboarding.
            JobReviewedTelemetryHelper.TrackAutoApproval(
                _telemetryClient,
                message.SyncJobId,
                syncJob.TargetOfficeGroupId,
                syncJob.MembershipType,
                JobReviewedTelemetryHelper.ToRequestType(message.Origin));
        }

        // Each setting switches on one way a part may qualify; with every path off nothing auto-approves.
        private async Task<bool> ShouldAutoApproveJobAsync(string query, string userIdentity, bool isGroupBasedAutoApprovalEnabled, bool isOrgLeaderAutoApprovalEnabled)
        {
            try
            {
                return await ShouldAutoApprovePerPartAsync(query, userIdentity, isGroupBasedAutoApprovalEnabled, isOrgLeaderAutoApprovalEnabled);
            }
            catch (Exception ex)
            {
                _logger.AutoApprovalCheckError(ex);
                return false;
            }
        }

        private async Task<bool> IsAutoApprovalForGroupBasedSyncsEnabledAsync()
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled);
                return setting != null ? bool.Parse(setting.SettingValue) : false;
            }
            catch (Exception ex)
            {
                _logger.AutoApprovalSettingRetrievalError(ex);
                return false;
            }
        }

        private async Task<bool> IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabledAsync()
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled);
                return setting != null ? bool.Parse(setting.SettingValue) : false;
            }
            catch (Exception ex)
            {
                _logger.OrgLeaderSettingRetrievalError(ex);
                return false;
            }
        }

        // IsPerPartAutoApprovalEnabled is no longer read; the member stays so existing rows are not orphaned.

        private async Task<bool> IsAutoApprovalForLeaderAssistantEnabledAsync()
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForLeaderAssistantEnabled);
                return setting != null && string.Equals(setting.SettingValue, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LeaderAssistantSettingRetrievalError(ex);
                return false;
            }
        }

        private async Task<bool> IsAutoApprovalForConfiguredAttributeEnabledAsync()
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForConfiguredAttributeEnabled);
                return setting != null && string.Equals(setting.SettingValue, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.ConfiguredAttributeSettingRetrievalError(ex);
                return false;
            }
        }

        // Approved only when every part qualifies through an enabled path; any failure rejects.
        private async Task<bool> ShouldAutoApprovePerPartAsync(string query, string userIdentity, bool isGroupBasedEnabled, bool isOrgLeaderEnabled)
        {
            try
            {
                if (!JsonParser.TryParseParts(query, out var parts, out var parseFailure) || parts.Count == 0)
                {
                    if (parseFailure == QueryParseFailure.None)
                        parseFailure = QueryParseFailure.NoPartsInQuery;

                    _logger.PerPartQueryNotParsable(parseFailure.ToString());
                    return false;
                }

                int? employeeId = null;
                string tableName = null;

                // Read only when a SQL part exists, so it costs nothing elsewhere.
                var isLeaderAssistantEnabled = false;
                var isConfiguredEnabled = false;
                var screening = new PathScreening();
                var sensitiveAttributes = new SensitiveAttributeCache();

                if (parts.Any(p => p.Type == SourcePart.SqlMembershipType))
                {
                    isLeaderAssistantEnabled = await IsAutoApprovalForLeaderAssistantEnabledAsync();
                    _logger.LeaderAssistantPathResolved(isLeaderAssistantEnabled);

                    isConfiguredEnabled = await IsAutoApprovalForConfiguredAttributeEnabledAsync();
                    _logger.ConfiguredAttributePathResolved(isConfiguredEnabled);

                    // Loaded even when screening is off so the logged count always reflects what is configured.
                    screening = await ResolvePathScreeningAsync();
                    await LoadSensitiveAttributesAsync(sensitiveAttributes, screening.Any);

                    _logger.SensitiveScreeningResolved(screening.OrgLeader, screening.LeaderAssistant, screening.ConfiguredAttribute, sensitiveAttributes.Names.Count);
                }

                // Resolved only when a SQL path is on, so a disabled path costs no lookup.
                if ((isOrgLeaderEnabled || isLeaderAssistantEnabled || isConfiguredEnabled) && parts.Any(p => p.Type == SourcePart.SqlMembershipType))
                {
                    tableName = await ResolveUsersTableNameAsync();

                    // Only the manager-self comparison needs the requestor's employee id.
                    if (isOrgLeaderEnabled && !string.IsNullOrEmpty(tableName))
                        employeeId = await ResolveEmployeeIdAsync(userIdentity, tableName);

                    _logger.SqlPartEvaluationContext(
                        string.IsNullOrEmpty(tableName) ? "(unresolved)" : tableName,
                        !isOrgLeaderEnabled ? "not read (manager-self path disabled)"
                            : employeeId != null ? "resolved" : "not found");
                }

                // One profile read per distinct leader per submission, shared across parts.
                var assistantCache = new Dictionary<int, AssistantLookup>();

                var mix = new PartApprovalMix();
                var configuredState = new ConfiguredAttributeState();

                for (var i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    PartRejection? rejection;

                    switch (part.Type)
                    {
                        case SourcePart.GroupMembershipType:
                            rejection = await EvaluateGroupPartAsync(part, userIdentity, isGroupBasedEnabled, i, mix);
                            break;
                        case SourcePart.SqlMembershipType:
                            rejection = await EvaluateSqlPartAsync(part, employeeId, userIdentity, tableName, isOrgLeaderEnabled, isLeaderAssistantEnabled, isConfiguredEnabled, screening, sensitiveAttributes, assistantCache, i, mix, configuredState);
                                                        break;
                        default:
                            rejection = Reject(part, i, SourcePartRejectionReason.Unknown_Type,
                                $"'{part.Type}' is not a source type that can be evaluated");
                            break;
                    }

                    if (rejection != null)
                    {
                        _logger.PerPartRuleDeclined(i, part.Type, part.SourceDescriptor, rejection.Value.ReasonText,
                            rejection.Value.Details, i + 1, parts.Count, mix.SqlManagerSelf, mix.SqlAssistant, mix.SqlConfigured, mix.GroupPublic, mix.GroupOwner);
                        return false;
                    }
                }

                _logger.PerPartRuleApproved(mix.SqlManagerSelf, mix.SqlAssistant, mix.SqlConfigured, mix.GroupPublic, mix.GroupOwner, parts.Count);
                return true;
            }
            catch (Exception ex)
            {
                _logger.PerPartRuleCheckError(ex);
                return false;
            }
        }

        // A path with no saved setting follows the legacy global switch, so existing deployments keep their behaviour.
        private async Task<PathScreening> ResolvePathScreeningAsync()
        {
            var global = await IsSensitiveAttributeScreeningEnabledAsync();

            return new PathScreening(
                await ReadPathScreeningAsync(SettingKey.IsSensitiveScreeningEnabledForOrgLeader, global),
                await ReadPathScreeningAsync(SettingKey.IsSensitiveScreeningEnabledForLeaderAssistant, global),
                await ReadPathScreeningAsync(SettingKey.IsSensitiveScreeningEnabledForConfiguredAttribute, global));
        }

        // Unreadable means screening stays on, so a Sensitive filter goes to manual review instead of being auto-approved.
        private async Task<bool> ReadPathScreeningAsync(SettingKey key, bool fallback)
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(key);
                return setting == null ? fallback : string.Equals(setting.SettingValue, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.SensitiveScreeningSettingRetrievalError(ex);
                return true;
            }
        }

        // Unreadable means screening stays on, same as the per-path read.
        private async Task<bool> IsSensitiveAttributeScreeningEnabledAsync()
        {
            try
            {
                var setting = await _databaseSettingsRepository.GetSettingByKeyAsync(SettingKey.IsSensitiveAttributeScreeningEnabled);
                return setting != null && string.Equals(setting.SettingValue, "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.SensitiveScreeningSettingRetrievalError(ex);
                return true;
            }
        }

        // Enabled is ignored: an attribute hidden from the query builder can still be Sensitive.
        private async Task<SensitiveAttributeCache> LoadSensitiveAttributesAsync(SensitiveAttributeCache cache, bool screeningEnabled)
        {
            if (cache.Loaded)
                return cache;

            cache.Loaded = true;

            try
            {
                var attributes = await _sqlMembershipSourcesRepository.GetDefaultSourceAttributesAsync();

                cache.Names = attributes?
                    .Where(a => a != null && a.IsSensitive && !string.IsNullOrWhiteSpace(a.Name))
                    .Select(a => a.Name)
                    .ToList() ?? new List<string>();
            }
            catch (Exception ex)
            {
                // Only an error when screening is on; with it off the list is informational and nothing fails closed.
                if (screeningEnabled)
                    _logger.SensitiveAttributeListReadError(ex);

                cache.Failed = true;
                cache.Names = new List<string>();
            }

            return cache;
        }

        // Called only after an identity check passed, so it never masks an identity mismatch.
        private PartRejection? ScreenFilter(SourcePart part, bool screeningEnabled, SensitiveAttributeCache cache, int partIndex)
        {
            if (!screeningEnabled || part.Filter == null)
                return null;

            if (cache.Failed)
                return Reject(part, partIndex, SourcePartRejectionReason.Sensitive_List_Unavailable,
                    "the list of Sensitive attributes could not be read, so the filter on this source could not be screened; the submission needs manual review");

            // An empty list is not a failure: nothing is marked Sensitive, so nothing is blocked.
            foreach (var name in cache.Names)
            {
                bool matched;

                try
                {
                    matched = Regex.IsMatch(part.Filter, $@"\b{Regex.Escape(name)}\b", RegexOptions.IgnoreCase, SensitiveMatchTimeout);
                }
                catch (RegexMatchTimeoutException)
                {
                    return Reject(part, partIndex, SourcePartRejectionReason.Sensitive_List_Unavailable,
                        "the filter on this source could not be screened within the time allowed; the submission needs manual review");
                }

                if (matched)
                {
                    _logger.SqlPartRejectedBySensitiveFilter(partIndex, name);
                    return Reject(part, partIndex, SourcePartRejectionReason.Sensitive_Filter,
                        $"the filter on this source references '{name}', which is marked Sensitive");
                }
            }

            return null;
        }

        // Approves when the requestor's configured attributes match the admin-configured values and the requestor
        // is in the manager's reporting chain. Returns null when the part qualifies.
        private async Task<PartRejection?> EvaluateConfiguredAttributeAsync(SourcePart part, string requestorObjectId, string tableName, ConfiguredAttributeState state)
        {
            try
            {
                if (!state.Loaded)
                {
                    state.Loaded = true;
                    state.UnavailableReason = await LoadConfiguredAttributeAsync(state, requestorObjectId, tableName);
                }

                if (state.UnavailableReason != null)
                    return ConfiguredRuleUnavailable(state.UnavailableReason);

                if (!state.ValuesMatch)
                    return new PartRejection(SourcePartRejectionReason.Configured_Values_Mismatch,
                        "the requestor's attribute values do not match the configured-attribute values");

                if (await IsInManagerOrgAsync(part.ManagerId.Value, tableName, state))
                    return null;

                return new PartRejection(SourcePartRejectionReason.Configured_Not_In_Manager_Chain,
                    $"the requestor is not within the configured depth of {state.MaxDepth}");
            }
            catch (Exception ex)
            {
                _logger.ConfiguredAttributeRuleError("evaluation_failed", ex);
                return ConfiguredRuleUnavailable("evaluation_failed");
            }
        }

        private static PartRejection ConfiguredRuleUnavailable(string reason) =>
            new(SourcePartRejectionReason.Configured_Rule_Unavailable,
                $"the configured-attribute path could not be evaluated ({reason}); the submission needs manual review");

        // Manager is level 1, so the allowed levels below the manager are queried as MaxDepth + 1.
        private async Task<bool> IsInManagerOrgAsync(int managerId, string tableName, ConfiguredAttributeState state)
        {
            if (state.InManagerOrg.TryGetValue(managerId, out var cached))
                return cached;

            var requestorId = state.RequestorEmployeeId.ToString();
            var members = await _sqlMembershipRepository.GetChildEntitiesAsync($"EmployeeId = {requestorId}", managerId, tableName, state.MaxDepth + 1);
            return state.InManagerOrg[managerId] = members.Any(member => member.PersonnelNumber == requestorId);
        }

        // Returns why the rule cannot apply to this submission, or null when it can.
        private async Task<string> LoadConfiguredAttributeAsync(ConfiguredAttributeState state, string requestorObjectId, string tableName)
        {
            ConfiguredAttributeApprovalConfiguration configuration;

            try
            {
                configuration = await _databaseSettingsRepository.GetConfiguredAttributeApprovalAsync();
            }
            catch (Exception ex)
            {
                _logger.ConfiguredAttributeRuleError("configuration_unavailable", ex);
                return "configuration_unavailable";
            }

            try
            {
                configuration.Validate();
            }
            catch (Exception ex)
            {
                _logger.ConfiguredAttributeRuleError("configuration_invalid", ex);
                return "configuration_invalid";
            }

            if (string.IsNullOrEmpty(tableName))
                return "table_unresolved";

            state.MaxDepth = configuration.MaxDepth;
            List<SqlMembershipAttribute> attributes;
            try
            {
                var settings = await _sqlMembershipSourcesRepository.GetDefaultSourceAttributesAsync();
                var columns = await _sqlMembershipRepository.GetColumnDetailsAsync(tableName);
                attributes = ConfiguredAttributeApprovalMetadata.Resolve(columns, settings);
            }
            catch (Exception ex)
            {
                _logger.ConfiguredAttributeRuleError("metadata_unavailable", ex);
                return "metadata_unavailable";
            }

            var rows = new List<(ConfiguredAttributeApprovalRow Row, SqlMembershipAttribute Attribute)>();
            foreach (var row in configuration.Attributes)
            {
                var attribute = attributes.FirstOrDefault(a => string.Equals(a.Name, row.Attribute, StringComparison.OrdinalIgnoreCase));
                if (!ConfiguredAttributeApprovalMetadata.IsEligible(attribute))
                    return "attribute_ineligible";
                rows.Add((row, attribute));
            }

            Dictionary<string, string> profile;
            try
            {
                profile = await _sqlMembershipRepository.GetUserAttributesAsync(requestorObjectId, tableName);
            }
            catch (Exception ex)
            {
                _logger.ConfiguredAttributeRuleError("profile_unavailable", ex);
                return "profile_unavailable";
            }

            if (profile == null)
                return "requestor_not_found";
            if (!profile.TryGetValue("EmployeeId", out var employeeIdText) || !int.TryParse(employeeIdText, out var employeeId))
                return "requestor_employee_id_unavailable";
            state.RequestorEmployeeId = employeeId;

            // Every configured attribute must match (AND); the requestor's value may match any configured value (OR).
            state.ValuesMatch = rows.All(entry =>
            {
                var column = ConfiguredAttributeApprovalMetadata.GetColumnName(entry.Attribute);
                if (!profile.TryGetValue(column, out var raw)
                    || !ConfiguredAttributeApprovalMetadata.TryCanonicalize(NormalizeRaw(raw, entry.Attribute.Type), entry.Attribute.Type, out var canonical))
                    return false;

                return entry.Row.Values.Any(value =>
                    ConfiguredAttributeApprovalMetadata.TryCanonicalize(value, entry.Attribute.Type, out var configured)
                    && string.Equals(configured, canonical, StringComparison.Ordinal));
            });

            return null;
        }

        // GetUserAttributesAsync stringifies values with ToString, so bit and date need mapping back to their canonical forms.
        private static string NormalizeRaw(string raw, string type)
        {
            switch (type?.ToLowerInvariant())
            {
                case "bit":
                    return bool.TryParse(raw, out var flag) ? (flag ? "1" : "0") : raw;
                case "date":
                    return DateTime.TryParse(raw, out var date) ? date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : raw;
                default:
                    return raw;
            }
        }

        private async Task<string> ResolveUsersTableNameAsync()
        {
            try
            {
                var adfRun = await _adfRunHistoryRepository.GetLatestAdfRunAsync();
                if (adfRun is null)
                {
                    _logger.RunIdRetrievalError(new InvalidOperationException("No eligible ADF run is available; see the AdfRunHistoryRepository warning for the cause."));
                    return null;
                }

                return adfRun.GetTableName();
            }
            catch (Exception ex)
            {
                _logger.RunIdRetrievalError(ex);
                return null;
            }
        }

        // Dedicated fail-closed boundary: any repository failure logs the specific EventId and yields
        // null, so it never surfaces as the generic PerPartRuleCheckError on the outer catch.
        private async Task<int?> ResolveEmployeeIdAsync(string azureObjectId, string tableName)
        {
            try
            {
                return await _sqlMembershipRepository.GetUserEmployeeIdAsync(azureObjectId, tableName);
            }
            catch (Exception ex)
            {
                _logger.EmployeeIdRetrievalError(ex);
                return null;
            }
        }

        // Returns null when the part is approved, otherwise the reason it was rejected and a
        // human-readable detail (group visibility, employee id mismatch, ...) explaining that reason.
        private async Task<PartRejection?> EvaluateGroupPartAsync(SourcePart part, string userIdentity, bool isGroupBasedEnabled, int partIndex, PartApprovalMix mix)
        {
            // Checked before the directory is touched, so a disabled path costs nothing.
            if (!isGroupBasedEnabled)
                return Reject(part, partIndex, SourcePartRejectionReason.Group_Path_Disabled,
                    "auto-approval for group-based sources is switched off, so this part has no enabled way to qualify; the submission needs manual review");

            if (!Guid.TryParse(part.SourceGroupId, out var groupId))
                return Reject(part, partIndex, SourcePartRejectionReason.Group_NotFound, "source id is not a valid group id");

            AzureADGroup group;
            try
            {
                var groups = await _graphGroupRepository.GetGroupsAsync(new List<Guid> { groupId });
                group = groups?.FirstOrDefault(g => g.ObjectId == groupId);
            }
            catch (Exception ex)
            {
                _logger.GroupVisibilityError(partIndex, part.SourceGroupId, ex);
                return new PartRejection(SourcePartRejectionReason.Group_LookupFailed, "visibility lookup threw an exception");
            }

            if (group == null)
                return Reject(part, partIndex, SourcePartRejectionReason.Group_NotFound, "group was not found in the directory");

            var visibility = string.IsNullOrWhiteSpace(group.Visibility) ? "unknown" : group.Visibility;

            if (string.Equals(group.Visibility, "Public", StringComparison.OrdinalIgnoreCase))
            {
                _logger.GroupPartApprovedByPublicVisibility(partIndex, part.SourceGroupId);
                mix.GroupPublic++;
                return null;
            }

            List<AzureADUser> owners;
            try
            {
                owners = await _graphGroupRepository.GetGroupOwnersAsync(groupId);
            }
            catch (Exception ex)
            {
                _logger.GroupOwnersError(partIndex, part.SourceGroupId, ex);
                return new PartRejection(SourcePartRejectionReason.Owner_LookupFailed, $"visibility {visibility}, owner lookup threw an exception");
            }

            if (owners != null && owners.Any(o => string.Equals(o.ObjectId.ToString(), userIdentity, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.GroupPartApprovedByOwner(partIndex, part.SourceGroupId, visibility);
                mix.GroupOwner++;
                return null;
            }

            return Reject(part, partIndex, SourcePartRejectionReason.Owner_CheckFailed,
                $"visibility {visibility} requires ownership, and the requestor is not an owner");
        }

        // Returns null when the part is approved, otherwise the reason it was rejected and a detail.
        private async Task<PartRejection?> EvaluateSqlPartAsync(
            SourcePart part,
            int? employeeId,
            string userIdentity,
            string tableName,
            bool isOrgLeaderEnabled,
            bool isLeaderAssistantEnabled,
            bool isConfiguredEnabled,
            PathScreening screening,
            SensitiveAttributeCache sensitiveAttributes,
            Dictionary<int, AssistantLookup> assistantCache,
            int partIndex,
            PartApprovalMix mix,
            ConfiguredAttributeState configuredState)
        {
            // Reported ahead of the data-driven reasons: the remedy is an admin setting.
            if (!isOrgLeaderEnabled && !isLeaderAssistantEnabled && !isConfiguredEnabled)
                return Reject(part, partIndex, SourcePartRejectionReason.Sql_Paths_Disabled,
                    "every auto-approval path for SQL sources is switched off, so this part has no enabled way to qualify; the submission needs manual review");

            // A filter-only SQL source names no manager, so neither SQL path has a leader to match.
            if (part.ManagerId == null)
                return Reject(part, partIndex, SourcePartRejectionReason.Sql_NoManagerId,
                    "the SQL source is filter-only and names no manager, so there is no manager relationship to verify; the submission needs manual review");

            // Requestor is the manager referenced in the query — approve. Only manager.id is compared;
            // filter, manager.depth and exclusionary are deliberately not considered. Ignoring them is
            // the safe direction: SqlMembershipRepository.GetChildEntitiesAsync seeds its recursive CTE
            // on this manager and recurses strictly downward (ON e.ManagerId = emp.EmployeeId), so a
            // filter or depth can only narrow the requestor's own subtree, never reach outside it.
            // Evaluated before the assistant path so manager-self never triggers a profile read.
            if (isOrgLeaderEnabled && employeeId != null && employeeId.Value == part.ManagerId.Value)
            {
                // Managing an org does not license a sensitive filter across it.
                var screened = ScreenFilter(part, screening.OrgLeader, sensitiveAttributes, partIndex);
                if (screened != null)
                    return screened;

                _logger.SqlPartApprovedByManagerSelf(partIndex);
                mix.SqlManagerSelf++;
                return null;
            }

            // One failure per path that ran; held back so a part another path approves is never logged as rejected.
            var failures = new List<PartRejection>();

            if (isOrgLeaderEnabled)
                failures.Add(employeeId == null
                    ? new PartRejection(SourcePartRejectionReason.EmployeeId_Missing, "the requestor has no EmployeeId in the users table")
                    : new PartRejection(SourcePartRejectionReason.Requestor_Manager_Mismatch, "the requestor is not the manager referenced in the query"));

            if (isLeaderAssistantEnabled)
            {
                var lookup = await ResolveAssistantsAsync(part.ManagerId.Value, tableName, assistantCache, partIndex);

                if (lookup.Status == AssistantLookupStatus.LeaderUnresolved)
                    failures.Add(new PartRejection(SourcePartRejectionReason.Manager_Identity_Unresolved,
                        "the manager referenced in the query has no resolvable directory identity, so an assistant relationship could not be verified"));
                else if (lookup.Status == AssistantLookupStatus.Unavailable)
                    failures.Add(new PartRejection(SourcePartRejectionReason.Assistant_Lookup_Unavailable,
                        "the manager's published assistant could not be read, so an assistant relationship could not be verified"));
                // The object id is the match key; the manager published this person as acting for them.
                else if (lookup.Assistants.Any(a => string.Equals(a.ObjectId.ToString(), userIdentity, StringComparison.OrdinalIgnoreCase)))
                {
                    // An assistant is further from the data owner, so screening applies here too.
                    var screened = ScreenFilter(part, screening.LeaderAssistant, sensitiveAttributes, partIndex);
                    if (screened != null)
                        return screened;

                    _logger.SqlPartApprovedByAssistant(partIndex);
                    mix.SqlAssistant++;
                    return null;
                }
                else
                    failures.Add(new PartRejection(SourcePartRejectionReason.Requestor_Not_Assistant,
                        "the requestor is not a published assistant of the manager referenced in the query"));
            }

            if (isConfiguredEnabled)
            {
                var configuredRejection = await EvaluateConfiguredAttributeAsync(part, userIdentity, tableName, configuredState);
                if (configuredRejection == null)
                {
                    var screened = ScreenFilter(part, screening.ConfiguredAttribute, sensitiveAttributes, partIndex);
                    if (screened != null)
                        return screened;

                    _logger.SqlPartApprovedByConfiguredAttribute(partIndex);
                    mix.SqlConfigured++;
                    return null;
                }

                failures.Add(configuredRejection.Value);
            }

            return Reject(part, partIndex, PartRejection.Combine(failures));
        }

        private async Task<AssistantLookup> ResolveAssistantsAsync(int managerId, string tableName, Dictionary<int, AssistantLookup> cache, int partIndex)
        {
            if (cache.TryGetValue(managerId, out var cached))
                return cached;

            var lookup = await LookupAssistantsAsync(managerId, tableName, partIndex);
            cache[managerId] = lookup;
            return lookup;
        }

        // Failures resolve to a status so neither can look like "this leader has no assistant".
        private async Task<AssistantLookup> LookupAssistantsAsync(int managerId, string tableName, int partIndex)
        {
            Guid leaderObjectId;

            try
            {
                if (string.IsNullOrEmpty(tableName))
                    throw new InvalidOperationException("The users table name could not be resolved.");

                // Only the leader's directory id is needed; GetOrgLeaderAsync would also walk the whole reporting tree for a depth this path discards.
                var azureObjectId = await _sqlMembershipRepository.GetUserAzureObjectIdAsync(managerId, tableName);

                if (!Guid.TryParse(azureObjectId, out leaderObjectId))
                    throw new InvalidOperationException("The manager has no usable directory object id in the users table.");
            }
            catch (Exception ex)
            {
                _logger.ManagerIdentityResolveError(partIndex, ex);
                return new AssistantLookup(AssistantLookupStatus.LeaderUnresolved, null);
            }

            try
            {
                var assistants = await _graphGroupRepository.GetAssistantsAsync(leaderObjectId);
                return new AssistantLookup(AssistantLookupStatus.Resolved, assistants ?? new List<AzureADUser>());
            }
            catch (Exception ex)
            {
                _logger.AssistantLookupError(partIndex, ex);
                return new AssistantLookup(AssistantLookupStatus.Unavailable, null);
            }
        }

        private PartRejection Reject(SourcePart part, int partIndex, SourcePartRejectionReason reason, string details) =>
            Reject(part, partIndex, new PartRejection(reason, details));

        private PartRejection Reject(SourcePart part, int partIndex, PartRejection rejection)
        {
            _logger.SourcePartRejected(partIndex, part.Type, part.SourceDescriptor, rejection.ReasonText, rejection.Details);
            return rejection;
        }
    }
}
