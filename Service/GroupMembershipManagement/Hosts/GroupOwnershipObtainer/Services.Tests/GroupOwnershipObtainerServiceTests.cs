// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Models;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Services;
using Services.Entities;
using System.Text.Json.Nodes;

namespace Tests.Services
{
    [TestClass]
    public class GroupOwnershipObtainerServiceTests
    {
        private Mock<IDryRunValue> _dryRunSettings = null!;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private Mock<IDatabaseGroupsRepository> _groupsRepository = null!;
        private Mock<IDatabaseChannelsRepository> _channelsRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private Mock<IBlobStorageRepository> _blobStorageRepository = null!;
        private GroupOwnershipObtainerService _groupOwnershipObtainerService = null!;

        [TestInitialize]
        public void Setup()
        {
            _dryRunSettings = new Mock<IDryRunValue>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _groupsRepository = new Mock<IDatabaseGroupsRepository>();
            _channelsRepository = new Mock<IDatabaseChannelsRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();

            _groupOwnershipObtainerService = new GroupOwnershipObtainerService(
                _dryRunSettings.Object,
                NullLogger<GroupOwnershipObtainerService>.Instance,
                _syncJobRepository.Object,
                _groupsRepository.Object,
                _channelsRepository.Object,
                _graphGroupRepository.Object,
                _blobStorageRepository.Object
                );
        }

        [TestMethod]
        [DataRow("All", "GroupMembership,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("Hybrid", "GroupMembership,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("GroupMembership", "GroupMembership,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("CustomType1", "GroupMembership,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("CustomType2", "GroupMembership,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("GroupMembership,CustomType1", "GroupMembership,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("GroupMembership,CustomType1,CustomType2", "GroupMembership,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        public void TestExistingFilterSyncJobsBySourceTypes(string requestedSourceTypes, string jobSourceTypes)
        {
            var requestedTypes = new HashSet<string>(requestedSourceTypes.Split(','));
            var jobTypes = new HashSet<string>(jobSourceTypes.Split(','));
            var jobs = GenerateSampleJobs(jobTypes);

            var groupIds = _groupOwnershipObtainerService.FilterSyncJobsBySourceTypes(requestedTypes, jobs);

            var expectedGroupIds = new List<Guid>();
            if (requestedSourceTypes.Contains("All"))
            {
                expectedGroupIds = jobs.Select(x => x.TargetOfficeGroupId)
                                       .ToList();
            }
            else if (requestedSourceTypes.Contains("Hybrid"))
            {
                var hybridJobs = jobs.Where(x =>
                {
                    if (x.Query == null) return false;

                    var queryTypes = GetQueryTypes(x.Query);

                    return queryTypes.Count() > 1;

                }).ToList();

                expectedGroupIds = hybridJobs.Select(x => x.TargetOfficeGroupId)
                                             .ToList();
            }
            else
            {
                var requestedJobs = jobs.Where(x =>
                {
                    if (x.Query == null) return false;

                    var queryTypes = GetQueryTypes(x.Query);

                    return requestedTypes.All(x => queryTypes.Contains(x, StringComparer.InvariantCultureIgnoreCase))
                    && requestedTypes.Count == queryTypes.Count;

                }).ToList();

                expectedGroupIds = requestedJobs.Select(x => x.TargetOfficeGroupId)
                                                .ToList();
            }

            Assert.AreEqual(expectedGroupIds.Count, groupIds.Count);
            Assert.IsTrue(groupIds.Count > 0);
        }

        [TestMethod]
        [DataRow("GroupMembership,CustomType2", "GroupMembership,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("CustomType1,CustomType2", "GroupMembership,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("type3", "GroupMembership,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        [DataRow("type3,GroupMembership", "GroupMembership,CustomType1,CustomType2", DisplayName = "RequestedSourceTypes,CurrentJobSourceTypes")]
        public void TestNonExistingFilterSyncJobsBySourceTypes(string requestedSourceTypes, string jobSourceTypes)
        {
            var requestedTypes = new HashSet<string>(requestedSourceTypes.Split(','));
            var jobTypes = new HashSet<string>(jobSourceTypes.Split(','));
            var jobs = GenerateSampleJobs(jobTypes);

            var groupIds = _groupOwnershipObtainerService.FilterSyncJobsBySourceTypes(requestedTypes, jobs);

            var expectedGroupIds = new List<Guid>();
            if (requestedSourceTypes.Contains("All"))
            {
                expectedGroupIds = jobs.Select(x => x.TargetOfficeGroupId)
                                       .ToList();
            }
            else if (requestedSourceTypes.Contains("Hybrid"))
            {
                var hybridJobs = jobs.Where(x =>
                {
                    if (x.Query == null) return false;

                    var queryTypes = GetQueryTypes(x.Query);

                    return queryTypes.Count() > 1;

                }).ToList();

                expectedGroupIds = hybridJobs.Select(x => x.TargetOfficeGroupId)
                                             .ToList();
            }
            else
            {
                var requestedJobs = jobs.Where(x =>
                {
                    if (x.Query == null) return false;

                    var queryTypes = GetQueryTypes(x.Query);

                    return requestedTypes.All(x => queryTypes.Contains(x, StringComparer.InvariantCultureIgnoreCase))
                    && requestedTypes.Count == queryTypes.Count;

                }).ToList();

                expectedGroupIds = requestedJobs.Select(x => x.TargetOfficeGroupId)
                                                .ToList();
            }

            Assert.AreEqual(expectedGroupIds.Count, groupIds.Count);
            Assert.AreEqual(0, groupIds.Count);
        }

        [TestMethod]
        public async Task GetGroupIdAsync_GroupMembership_ReturnsGroupId()
        {
            var expectedGroupId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                MembershipType = MembershipTypes.GroupMembership.ToString()
            };

            _groupsRepository
                .Setup(x => x.GetGroupUsingSyncJobIdAsync(syncJob.Id))
                .ReturnsAsync(new Group { GroupId = expectedGroupId });

            var result = await _groupOwnershipObtainerService.GetGroupIdAsync(syncJob);

            Assert.AreEqual(expectedGroupId, result);
            _groupsRepository.Verify(x => x.GetGroupUsingSyncJobIdAsync(syncJob.Id), Times.Once);
        }

        [TestMethod]
        public async Task GetGroupIdAsync_TeamsChannel_ReturnsChannelGroupId()
        {
            var expectedGroupId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                MembershipType = MembershipTypes.TeamsChannelMembership.ToString()
            };

            _channelsRepository
                .Setup(x => x.GetChannelUsingSyncJobIdAsync(syncJob.Id))
                .ReturnsAsync(new Channel { GroupId = expectedGroupId });

            var result = await _groupOwnershipObtainerService.GetGroupIdAsync(syncJob);

            Assert.AreEqual(expectedGroupId, result);
        }

        [TestMethod]
        public async Task GetSyncJobsSegmentAsync_ReturnsJobsList()
        {
            var jobs = new List<SyncJob>
            {
                new SyncJob { Id = Guid.NewGuid() },
                new SyncJob { Id = Guid.NewGuid() }
            };
            _syncJobRepository
                .Setup(x => x.GetSyncJobsAsync(true, SyncStatus.All))
                .ReturnsAsync(jobs);

            var result = await _groupOwnershipObtainerService.GetSyncJobsSegmentAsync();

            Assert.AreEqual(2, result.Count);
        }

        [TestMethod]
        public async Task GetGroupOwnersAsync_GroupExists_ReturnsOwnerIds()
        {
            var groupId = Guid.NewGuid();
            var ownerA = Guid.NewGuid();
            var ownerB = Guid.NewGuid();

            _graphGroupRepository.Setup(x => x.GroupExists(groupId)).ReturnsAsync(true);
            _graphGroupRepository
                .Setup(x => x.GetGroupOwnersAsync(groupId, 100))
                .ReturnsAsync(new List<AzureADUser>
                {
                    new AzureADUser { ObjectId = ownerA },
                    new AzureADUser { ObjectId = ownerB }
                });

            var result = await _groupOwnershipObtainerService.GetGroupOwnersAsync(groupId);

            Assert.AreEqual(2, result.Count);
            CollectionAssert.Contains(result, ownerA);
            CollectionAssert.Contains(result, ownerB);
        }

        [TestMethod]
        public async Task GetGroupOwnersAsync_GroupDoesNotExist_ReturnsEmptyList()
        {
            var groupId = Guid.NewGuid();
            _graphGroupRepository.Setup(x => x.GroupExists(groupId)).ReturnsAsync(false);

            var result = await _groupOwnershipObtainerService.GetGroupOwnersAsync(groupId);

            Assert.AreEqual(0, result.Count);
            _graphGroupRepository.Verify(x => x.GetGroupOwnersAsync(It.IsAny<Guid>(), It.IsAny<int>()), Times.Never);
        }

        [TestMethod]
        public async Task SendMembershipAsync_UploadsFile_ReturnsFileName()
        {
            var groupId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Query = "[]"
            };
            var users = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
            _dryRunSettings.SetupGet(x => x.DryRunEnabled).Returns(false);
            _blobStorageRepository
                .Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns(Task.CompletedTask);

            var fileName = await _groupOwnershipObtainerService.SendMembershipAsync(syncJob, groupId, users, 1, false);

            Assert.IsTrue(fileName.Contains(groupId.ToString()));
            Assert.IsTrue(fileName.EndsWith("_GroupOwnershipObtainer_1.json"));
            _blobStorageRepository.Verify(x => x.UploadFileAsync(
                It.Is<string>(s => s == fileName),
                It.IsAny<string>(),
                It.IsAny<Dictionary<string, string>>()), Times.Once);
        }

        [TestMethod]
        public async Task SendMembershipAsync_NullUsers_UsesEmptyList()
        {
            var groupId = Guid.NewGuid();
            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                Query = "[]"
            };
            _dryRunSettings.SetupGet(x => x.DryRunEnabled).Returns(true);
            _blobStorageRepository
                .Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Dictionary<string, string>>()))
                .Returns(Task.CompletedTask);

            var fileName = await _groupOwnershipObtainerService.SendMembershipAsync(syncJob, groupId, null, 2, true);

            Assert.IsTrue(fileName.EndsWith("_GroupOwnershipObtainer_2.json"));
        }

        private List<JobsFilterSyncJob> GenerateSampleJobs(IEnumerable<string> sourceTypes)
        {
            var jobs = new List<JobsFilterSyncJob>();
            var queryParts = new List<string>();

            foreach (var sourceType in sourceTypes)
            {
                var queryPart = $"{{\"type\":\"{sourceType}\",\"source\":\"source-definition\"}}";
                queryParts.Add(queryPart);

                // individual sources
                jobs.Add(new JobsFilterSyncJob
                {
                    TargetOfficeGroupId = Guid.NewGuid(),
                    Query = $"[{queryPart}]"
                });

                // hybrid jobs
                if (queryParts.Count > 1)
                {
                    jobs.Add(new JobsFilterSyncJob
                    {
                        TargetOfficeGroupId = Guid.NewGuid(),
                        Query = $"[{string.Join(",", queryParts)}]"
                    });
                }
            }

            return jobs;
        }

        private List<string> GetQueryTypes(string query)
        {
            var queryParts = JsonNode.Parse(query).AsArray();
            var queryTypes = queryParts.Select(x => x["type"])
                                       .OfType<JsonValue>()
                                       .Select(x => x.GetValue<string>())
                                       .Distinct()
                                       .ToList();

            return queryTypes;
        }
    }
}
