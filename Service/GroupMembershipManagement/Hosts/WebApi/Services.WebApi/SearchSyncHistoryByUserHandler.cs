// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Models.Helpers;
using Repositories.Contracts;
using Services.Contracts;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;

namespace Services
{
    public class SearchSyncHistoryByUserHandler : RequestHandlerBase<SearchSyncHistoryByUserRequest, SearchSyncHistoryByUserResponse>
    {
        private const int ProgressUpdateIntervalRuns = 25;

        private readonly ILoggingRepository _loggingRepository;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly ISyncJobHistoryRepository _syncJobHistoryRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IHubContext<SignalRService> _hubContext;

        public SearchSyncHistoryByUserHandler(
            ILoggingRepository loggingRepository,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            ISyncJobHistoryRepository syncJobHistoryRepository,
            IBlobStorageRepository blobStorageRepository,
            IGraphGroupRepository graphGroupRepository,
            IHubContext<SignalRService> hubContext) : base(loggingRepository)
        {
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _syncJobHistoryRepository = syncJobHistoryRepository ?? throw new ArgumentNullException(nameof(syncJobHistoryRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        }

        protected override async Task<SearchSyncHistoryByUserResponse> ExecuteCoreAsync(SearchSyncHistoryByUserRequest request)
        {
            var response = new SearchSyncHistoryByUserResponse();

            try
            {
                var syncJob = await _databaseSyncJobsRepository.GetSyncJobAsync(request.SyncJobId);
                if (syncJob == null)
                {
                    response.StatusCode = HttpStatusCode.NotFound;
                    return response;
                }

                var allRuns = await GetAllHistoryAsync(request.SyncJobId);
                var targetGroupId = syncJob.TargetOfficeGroupId.ToString();
                var totalRuns = allRuns.Count;
                var processedRuns = 0;
                var lastPublishedProcessedRuns = 0;
                var lastPublishedPercent = GetProgressPercent(processedRuns, totalRuns);

                await PublishProgressAsync(request, processedRuns, totalRuns, response.MatchingRunIds.Count, completed: false);

                foreach (var run in allRuns)
                {
                    if (run.RunId == Guid.Empty)
                    {
                        processedRuns++;
                        if (ShouldPublishProgress(processedRuns, lastPublishedProcessedRuns, totalRuns, lastPublishedPercent))
                        {
                            await PublishProgressAsync(request, processedRuns, totalRuns, response.MatchingRunIds.Count, completed: false);
                            lastPublishedProcessedRuns = processedRuns;
                            lastPublishedPercent = GetProgressPercent(processedRuns, totalRuns);
                        }

                        continue;
                    }

                    var runMembershipChange = await GetRunMembershipChangeAsync(targetGroupId, run.RunId, request.UserObjectId);
                    if (runMembershipChange != null)
                    {
                        response.MatchingRunIds.Add(run.RunId);
                        response.RunMembershipChanges.Add(runMembershipChange);
                    }

                    processedRuns++;
                    if (ShouldPublishProgress(processedRuns, lastPublishedProcessedRuns, totalRuns, lastPublishedPercent))
                    {
                        await PublishProgressAsync(request, processedRuns, totalRuns, response.MatchingRunIds.Count, completed: false);
                        lastPublishedProcessedRuns = processedRuns;
                        lastPublishedPercent = GetProgressPercent(processedRuns, totalRuns);
                    }
                }

                if (response.MatchingRunIds.Count == 0)
                {
                    response.UserInCurrentGroup = await _graphGroupRepository.IsEmailRecipientMemberOfGroupAsync(
                        request.UserObjectId.ToString(),
                        syncJob.TargetOfficeGroupId);
                    response.CheckedCurrentGroupMembership = true;
                }

                response.StatusCode = HttpStatusCode.OK;
                await PublishProgressAsync(request, processedRuns, totalRuns, response.MatchingRunIds.Count, completed: true);
            }
            catch (Exception ex)
            {
                await _loggingRepository.LogMessageAsync(new LogMessage
                {
                    Message = $"Error searching run history for SyncJobId={request.SyncJobId}, UserObjectId={request.UserObjectId}: {ex.Message}",
                });

                response.StatusCode = HttpStatusCode.InternalServerError;
            }

            return response;
        }

        private static bool ShouldPublishProgress(int processedRuns, int lastPublishedProcessedRuns, int totalRuns, int lastPublishedPercent)
        {
            if (processedRuns <= lastPublishedProcessedRuns)
            {
                return false;
            }

            if ((processedRuns - lastPublishedProcessedRuns) >= ProgressUpdateIntervalRuns)
            {
                return true;
            }

            return GetProgressPercent(processedRuns, totalRuns) > lastPublishedPercent;
        }

        private static int GetProgressPercent(int processedRuns, int totalRuns)
        {
            if (totalRuns <= 0)
            {
                return 100;
            }

            return (int)Math.Floor((double)processedRuns * 100 / totalRuns);
        }

        private Task PublishProgressAsync(
            SearchSyncHistoryByUserRequest request,
            int processedRuns,
            int totalRuns,
            int matchingRuns,
            bool completed)
        {
            if (string.IsNullOrWhiteSpace(request.RequestId))
            {
                return Task.CompletedTask;
            }

            var groupName = SignalRService.BuildSyncHistorySearchGroupName(request.RequestId);
            var payload = new SyncHistorySearchProgressUpdate
            {
                RequestId = request.RequestId,
                SyncJobId = request.SyncJobId,
                UserObjectId = request.UserObjectId,
                ProcessedRuns = processedRuns,
                TotalRuns = totalRuns,
                MatchingRuns = matchingRuns,
                Completed = completed,
            };

            return _hubContext.Clients.Group(groupName).SendAsync(SignalRService.SyncHistorySearchProgressEvent, payload);
        }

        private async Task<List<Models.SyncJobHistory.SyncJobHistory>> GetAllHistoryAsync(Guid syncJobId)
        {
            const int pageSize = 200;
            var pageNumber = 1;
            var allRuns = new List<Models.SyncJobHistory.SyncJobHistory>();

            while (true)
            {
                var page = await _syncJobHistoryRepository.GetBySyncJobIdAsync(syncJobId, pageSize, pageNumber);
                if (page == null || page.Count == 0)
                {
                    break;
                }

                allRuns.AddRange(page);
                if (page.Count < pageSize)
                {
                    break;
                }

                pageNumber++;
            }

            return allRuns;
        }

        private async Task<SearchSyncHistoryByUserRunMembershipChange?> GetRunMembershipChangeAsync(string targetGroupId, Guid runId, Guid userObjectId)
        {
            var blobResult = await _blobStorageRepository.FindAggregatedFileByRunIdAsync(targetGroupId, runId.ToString());
            if (blobResult.BlobStatus == BlobStatus.NotFound || string.IsNullOrWhiteSpace(blobResult.Path))
            {
                return null;
            }

            var fileContent = await _blobStorageRepository.DownloadFileAsync(blobResult.Path);
            if (fileContent.BlobStatus == BlobStatus.NotFound || string.IsNullOrWhiteSpace(fileContent.Content))
            {
                return null;
            }

            var membershipJson = TryDecompress(fileContent.Content);
            return GetRunMembershipChange(membershipJson, userObjectId, runId);
        }

        private static string TryDecompress(string content)
        {
            try
            {
                return TextCompressor.Decompress(content);
            }
            catch (FormatException)
            {
                return content;
            }
        }

        private static SearchSyncHistoryByUserRunMembershipChange? GetRunMembershipChange(string json, Guid userObjectId, Guid runId)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (!TryGetPropertyCaseInsensitive(document.RootElement, "SourceMembers", out var sourceMembers)
                    || sourceMembers.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                MembershipChangeType? membershipChangeType = null;

                foreach (var member in sourceMembers.EnumerateArray())
                {
                    if (!TryReadObjectId(member, out var memberObjectId) || memberObjectId != userObjectId)
                    {
                        continue;
                    }

                    if (TryReadMembershipAction(member, out var membershipAction)
                        && (membershipAction == MembershipAction.Add || membershipAction == MembershipAction.Remove))
                    {
                        membershipChangeType = MapMembershipChangeType(membershipAction);
                    }
                }

                if (!membershipChangeType.HasValue)
                {
                    return null;
                }

                return new SearchSyncHistoryByUserRunMembershipChange
                {
                    RunId = runId,
                    MembershipChangeType = membershipChangeType.Value,
                };
            }
            catch (JsonException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static MembershipChangeType MapMembershipChangeType(MembershipAction membershipAction)
        {
            return membershipAction == MembershipAction.Add
                ? MembershipChangeType.Added
                : MembershipChangeType.Removed;
        }

        private static bool TryReadObjectId(JsonElement member, out Guid objectId)
        {
            objectId = Guid.Empty;
            if (!TryGetPropertyCaseInsensitive(member, "ObjectId", out var objectIdElement)
                || objectIdElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            return Guid.TryParse(objectIdElement.GetString(), out objectId);
        }

        private static bool TryReadMembershipAction(JsonElement member, out MembershipAction action)
        {
            action = MembershipAction.None;
            if (!TryGetPropertyCaseInsensitive(member, "MembershipAction", out var actionElement))
            {
                return false;
            }

            if (actionElement.ValueKind == JsonValueKind.Number)
            {
                if (actionElement.TryGetInt32(out var actionInt)
                    && Enum.IsDefined(typeof(MembershipAction), actionInt))
                {
                    action = (MembershipAction)actionInt;
                    return true;
                }

                return false;
            }

            if (actionElement.ValueKind == JsonValueKind.String)
            {
                var actionString = actionElement.GetString();
                return Enum.TryParse(actionString, true, out action);
            }

            return false;
        }

        private static bool TryGetPropertyCaseInsensitive(JsonElement element, string propertyName, out JsonElement value)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }
    }
}