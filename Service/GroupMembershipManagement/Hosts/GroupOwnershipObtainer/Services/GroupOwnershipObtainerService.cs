// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Hosts.GroupOwnershipObtainer;
using Microsoft.Extensions.Logging;
using Models;
using Models.ServiceBus;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Entities;
using SqlMembershipObtainer.Entities;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Services
{
    public class GroupOwnershipObtainerService : IGroupOwnershipObtainerService
    {
        private const int JobsBatchSize = 20;
        private readonly IDryRunValue _dryRunSettings;
        private readonly ILogger<GroupOwnershipObtainerService> _logger;
        private readonly IDatabaseSyncJobsRepository _databaseSyncJobsRepository;
        private readonly IDatabaseGroupsRepository _databaseGroupsRepository;
        private readonly IDatabaseChannelsRepository _databaseChannelsRepository;
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly IBlobStorageRepository _blobStorageRepository;

        public GroupOwnershipObtainerService(
            IDryRunValue dryRunSettings,
            ILogger<GroupOwnershipObtainerService> logger,
            IDatabaseSyncJobsRepository databaseSyncJobsRepository,
            IDatabaseGroupsRepository databaseGroupsRepository,
            IDatabaseChannelsRepository databaseChannelsRepository,
            IGraphGroupRepository graphGroupRepository,
            IBlobStorageRepository blobStorageRepository)
        {
            _dryRunSettings = dryRunSettings ?? throw new ArgumentNullException(nameof(dryRunSettings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _databaseSyncJobsRepository = databaseSyncJobsRepository ?? throw new ArgumentNullException(nameof(databaseSyncJobsRepository));
            _databaseGroupsRepository = databaseGroupsRepository ?? throw new ArgumentNullException(nameof(databaseGroupsRepository));
            _databaseChannelsRepository = databaseChannelsRepository ?? throw new ArgumentNullException(nameof(databaseChannelsRepository));
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _blobStorageRepository = blobStorageRepository ?? throw new ArgumentNullException(nameof(blobStorageRepository));
        }

        public async Task<Guid> GetGroupIdAsync(SyncJob syncJob)
        {
            if (syncJob.MembershipType == MembershipTypes.TeamsChannelMembership.ToString())
            {
                var channel = syncJob.Channel ?? await _databaseChannelsRepository.GetChannelUsingSyncJobIdAsync(syncJob.Id);
                return channel.GroupId;
            }
            else if (syncJob.MembershipType == MembershipTypes.GroupMembership.ToString())
            {
                var group = syncJob.Group ?? await _databaseGroupsRepository.GetGroupUsingSyncJobIdAsync(syncJob.Id);
                return group.GroupId;
            }
            return Guid.Empty;
        }

        public async Task<List<SyncJob>> GetSyncJobsSegmentAsync()
        {
            var jobs = await _databaseSyncJobsRepository.GetSyncJobsAsync(true, SyncStatus.All);
            return jobs.ToList();
        }

        public async Task<List<Guid>> GetGroupOwnersAsync(Guid groupId)
        {
            var groupExists = await _graphGroupRepository.GroupExists(groupId);
            if (!groupExists)
            {
                _logger.GroupDoesNotExist(groupId);
                return new List<Guid>();
            }

            var owners = await _graphGroupRepository.GetGroupOwnersAsync(groupId, 100);
            return owners.Select(x => x.ObjectId).ToList();
        }

        public async Task<string> SendMembershipAsync(SyncJob syncJob, Guid groupId, List<Guid> allUsers, int currentPart, bool exclusionary)
        {
            var runId = syncJob.RunId.GetValueOrDefault();
            var groupMembership = new GroupMembership
            {
                SourceMembers = allUsers != null ? allUsers.Select(x => new AzureADUser { ObjectId = x }).ToList() : new List<AzureADUser>(),
                Destination = new AzureADGroup { ObjectId = groupId },
                RunId = runId,
                Exclusionary = exclusionary,
                SyncJobId = syncJob.Id,
                MembershipObtainerDryRunEnabled = _dryRunSettings.DryRunEnabled,
                Query = syncJob.Query
            };

            var timeStamp = DateTime.UtcNow.ToString("MMddyyyy-HHmm");
            var fileName = $"/{groupId}/{timeStamp}_{runId}_GroupOwnershipObtainer_{currentPart}.json";
            await _blobStorageRepository.UploadFileAsync(fileName, JsonSerializer.Serialize(groupMembership));

            return fileName;
        }

        public List<Guid> FilterSyncJobsBySourceTypes(HashSet<string> requestedSourceTypes, List<JobsFilterSyncJob> syncJobs)
        {
            var filteredJobs = new ConcurrentBag<Guid>();
            Parallel.ForEach(syncJobs, job =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(job.Query))
                        return;

                    var queryParts = JsonNode.Parse(job.Query).AsArray();
                    var queryTypes = queryParts.Select(x => x["type"])
                                               .OfType<JsonValue>()
                                               .Select(x => x.GetValue<string>())
                                               .Distinct()
                                               .ToList();

                    var includeJob = ApplyRules(requestedSourceTypes, new HashSet<string>(queryTypes));
                    if (includeJob)
                    {
                        filteredJobs.Add(job.TargetOfficeGroupId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.UnableToDetermineJobType(ex, job.TargetOfficeGroupId);
                }
            });

            return filteredJobs.Select(x => x).Distinct().ToList();
        }

        private bool ApplyRules(HashSet<string> requestedSourceTypes, HashSet<string> currentJobSourceTypes)
        {
            if (requestedSourceTypes.Contains("All", StringComparer.InvariantCultureIgnoreCase))
            {
                return true;
            }
            else if (requestedSourceTypes.Contains("Hybrid", StringComparer.InvariantCultureIgnoreCase))
            {
                return currentJobSourceTypes.Count > 1;
            }
            else
            {
                return requestedSourceTypes.All(x => currentJobSourceTypes.Contains(x, StringComparer.InvariantCultureIgnoreCase))
                       && requestedSourceTypes.Count == currentJobSourceTypes.Count;
            }
        }
    }
}