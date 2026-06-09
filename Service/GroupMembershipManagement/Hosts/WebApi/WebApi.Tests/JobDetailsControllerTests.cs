// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.JsonPatch.Operations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph.Models;
using MockQueryable.Moq;
using Models;
using Models.Entities;
using Models.SyncJobChange;
using Models.SyncJobHistory;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.EntityFramework;
using Services.Contracts;
using Services.Messages.Responses;
using Services.Messages.Requests;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Data;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using WebApi.Controllers.v1.Jobs;
using WebApi.Models;
using WebApi.Models.DTOs;
using Channel = Models.Channel;
using Roles = WebApi.Models.Roles;
using SyncJob = Models.SyncJob;
using Setting = Models.Setting;
using SyncJobDetails = WebApi.Models.DTOs.SyncJobDetails;
using Group = Models.Group;
using Title = Models.Title;

namespace Services.Tests
{
    [TestClass]
    public class JobDetailsControllerTests
    {
        private SyncJob _jobEntity = null!;
        private Group _group = null!;
        private Channel _channel = null!;
        private List<Title> _titles = null!;
        private SyncJobChange _syncJobChange = null!;
        private JobDetailsController _jobDetailsController = null!;
        private GetJobDetailsHandler _getJobDetailsHandler = null!;
        private GetGroupHandler _getGroupHandler = null!;
        private GetChannelHandler _getChannelHandler = null!;
        private PatchJobHandler _patchJobHandler = null!;
        private GetJobChangesHandler _getJobChangesHandler = null!;
        private GetSyncJobHistoryHandler _getSyncJobHistoryHandler = null!;
        private RemoveGMMHandler _removeGMMHandler = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private Mock<IDatabaseGroupsRepository> _groupRepository = null!;
        private Mock<IDatabaseChannelsRepository> _channelRepository = null!;
        private Mock<ISyncJobChangeRepository> _syncJobChangeRepository = null!;
        private Mock<ISyncJobHistoryRepository> _syncJobHistoryRepository = null!;
        private Mock<IBlobStorageRepository> _blobStorageRepository = null!;
        private GetMembershipDownloadHandler _getMembershipDownloadHandler = null!;
        private Mock<IRequestHandler<GetThresholdNotificationRequest, GetThresholdNotificationResponse>> _getThresholdNotificationHandlerMock = null!;
        private Mock<INotificationRepository> _notificationRepository = null!;
        private Mock<IDatabaseTitlesRepository> _titlesRepository = null!;
        private Mock<IDatabaseSettingsRepository> _settingsRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private Mock<ITeamsChannelRepository> _teamsChannelRepository = null!;
        private Mock<INotificationService> _notificationService = null!;
        private Mock<IThresholdConfig> _thresholdConfig = null!;
        private Mock<IHandleInactiveJobsConfig> _handleInactiveJobsConfig = null!;
        private bool _isGroupOwner = true;
        private Mock<IHttpContextAccessor> _httpContextAccessor = null!;
        private List<SyncJobHistory> _syncJobHistoryEntries = null!;

        private PatchJobRequestDTO CreatePatchJobRequestDTO(List<PatchOperation> operations, string changeReason, string businessJustification)
        {
            return new PatchJobRequestDTO
            {
                PatchOperation = operations,
                ChangeReason = changeReason,
                BusinessJustification = businessJustification
            };
        }

        private JsonElement? ConvertToJsonElement(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            // If value looks like JSON (starts with { or [), use as-is
            var trimmed = value.Trim();
            if ((trimmed.StartsWith("{") || trimmed.StartsWith("[")) &&
                (trimmed.EndsWith("}") || trimmed.EndsWith("]")))
            {
                return JsonDocument.Parse(value).RootElement;
            }

            // Otherwise, wrap it as a JSON string
            return JsonDocument.Parse($"\"{value}\"").RootElement;
        }

        [TestInitialize]
        public void Initialize()
        {
            _httpContextAccessor = new Mock<IHttpContextAccessor>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _groupRepository = new Mock<IDatabaseGroupsRepository>();
            _groupRepository = new Mock<IDatabaseGroupsRepository>();
            _channelRepository = new Mock<IDatabaseChannelsRepository>();
            _syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            _syncJobHistoryRepository = new Mock<ISyncJobHistoryRepository>();
            _blobStorageRepository = new Mock<IBlobStorageRepository>();
            _getMembershipDownloadHandler = new GetMembershipDownloadHandler(_loggingRepository.Object, _syncJobRepository.Object, _blobStorageRepository.Object);
            _titlesRepository = new Mock<IDatabaseTitlesRepository>();
            _settingsRepository = new Mock<IDatabaseSettingsRepository>();
            _notificationService = new Mock<INotificationService>();
            _thresholdConfig = new Mock<IThresholdConfig>();
            _handleInactiveJobsConfig = new Mock<IHandleInactiveJobsConfig>();
            _handleInactiveJobsConfig.Setup(x => x.NumberOfDaysBeforePurging).Returns(30);

            _graphGroupRepository = new Mock<IGraphGroupRepository>();

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>()))
                                    .ReturnsAsync(() => _isGroupOwner);

            _graphGroupRepository.Setup(x => x.GetGroupNameAsync(It.IsAny<Guid>()))
                                    .ReturnsAsync(() => "Group Name");

            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAITitleEnabled))
                                   .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAITitleEnabled, SettingValue = "true" });

            // Setup default threshold config
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToNotify).Returns(3);

            _teamsChannelRepository = new Mock<ITeamsChannelRepository>();

            _jobEntity = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = SyncStatus.Idle.ToString(),
                LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-4),
                LastSuccessfulStartTime = DateTime.UtcNow.AddHours(-5),
                StartDate = DateTime.UtcNow.AddMonths(-1),
                Query = "",
                ThresholdViolations = 0,
                ThresholdPercentageForAdditions = 10,
                ThresholdPercentageForRemovals = 10,
                Period = 6,
                Requestor = "example@microsoft.com",
                MembershipType = "GroupMembership"
            };

            _jobEntity.Group = new Group
            {
                GroupId = Guid.NewGuid(),
                SyncJobId = _jobEntity.Id
            };

            _group = new Group
            {
                GroupId = Guid.NewGuid(),
                SyncJobId = _jobEntity.Id
            };

            _channel = new Channel
            {
                GroupId = Guid.NewGuid(),
                ChannelId = "channel1",
                SyncJobId = _jobEntity.Id
            };

            _syncJobChange = new SyncJobChange
            {
                Id = new Guid(),
                ChangedByDisplayName = "Test",
                ChangedByObjectId = Guid.NewGuid(),
                ChangeReason = SyncJobChangeReason.Update.ToString(),
                SyncJobId = _jobEntity.Id,
                ChangeDetails = SyncJobSerializationHelper.SerializeSyncJob(_jobEntity),
                ChangeSource = SyncJobChangeSource.WebApp
            };

            var changes = new RepositoryPage<SyncJobChange>
            {
                Items = new List<SyncJobChange>
                {
                    _syncJobChange
                },
                PageNumber = 1,
                PageSize = 10,
                TotalCount = 1,
                TotalPages = 1
             };

            _syncJobHistoryEntries = new List<SyncJobHistory>
            {
                new SyncJobHistory
                {
                    Id = Guid.NewGuid(),
                    SyncJobId = _jobEntity.Id,
                    RunId = Guid.NewGuid(),
                    StartTime = DateTime.UtcNow.AddMinutes(-15),
                    EndTime = DateTime.UtcNow,
                    Duration = 900,
                    Status = SyncStatus.Idle.ToString(),
                    UsersAdded = 2,
                    UsersRemoved = 1,
                    ThresholdViolations = 0,
                    UpdatedByFunction = "GraphUpdater",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-15),
                    UpdatedAt = DateTime.UtcNow
                }
            };

            _titles = new List<Title>()
            {
                new Title
                {
                    Id = Guid.NewGuid(),
                    Name = "Title 1",
                    PartId = Guid.NewGuid(),
                    SyncJobId = _jobEntity.Id
                },
                new Title
                {
                    Id = Guid.NewGuid(),
                    Name = "Title 2",
                    PartId = Guid.NewGuid(),
                    SyncJobId = _jobEntity.Id
                }
            };

            _syncJobChangeRepository.Setup(x => x.GetPageBySyncJobId(_jobEntity.Id, 1, 10, SyncJobChangeSortingField.ChangeTime, false))
                            .ReturnsAsync(changes);

            _syncJobChangeRepository.Setup(x => x.GetLastSyncJobChangeBySyncJobIdAsync(It.IsAny<Guid>()))
                                    .ReturnsAsync(() => _syncJobChange);

            _syncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_jobEntity.Id, It.IsAny<int>(), It.IsAny<int>()))
                                     .ReturnsAsync(() => _syncJobHistoryEntries);

            _groupRepository.Setup(x => x.GetGroupAsync(It.IsAny<Guid>()))
                              .ReturnsAsync(() => _group);

            _channelRepository.Setup(x => x.GetChannelAsync(It.IsAny<Guid>(), It.IsAny<string>()))
                             .ReturnsAsync(() => _channel);

            _syncJobRepository.Setup(x => x.GetSyncJobAsync(_jobEntity.Id))
                              .ReturnsAsync(() => _jobEntity);

            _titlesRepository.Setup(x => x.GetTitlesAsync(It.IsAny<Guid>()))
                             .ReturnsAsync(() => _titles);

            _syncJobRepository.Setup(x => x.GetSyncJobs(It.IsAny<bool>()))
                              .Returns(() =>
                              {
                                  var jobs = new List<SyncJob> { _jobEntity };
                                  return jobs.BuildMock();
                              });

            _syncJobRepository.Setup(x => x.UpdateSyncJobsAsync(It.IsAny<IEnumerable<SyncJob>>(), It.IsAny<SyncStatus?>()))
                .Callback<IEnumerable<SyncJob>, SyncStatus?>((jobs, status) =>
                {
                    var updatedJob = jobs.FirstOrDefault(j => j.Id == _jobEntity.Id);
                    if (updatedJob != null)
                    {
                        _jobEntity = updatedJob;
                    }
                });

            _syncJobRepository.Setup(x => x.DeleteSyncJobAsync(It.IsAny<SyncJob>()));

            _getJobDetailsHandler = new GetJobDetailsHandler(_loggingRepository.Object,
                                                             _syncJobRepository.Object,
                                                             _syncJobChangeRepository.Object,
                                                             _titlesRepository.Object,
                                                             _graphGroupRepository.Object,
                                                             _teamsChannelRepository.Object,
                                                             _httpContextAccessor.Object,
                                                             _handleInactiveJobsConfig.Object);

            _patchJobHandler = new PatchJobHandler(_loggingRepository.Object,
                                                   _graphGroupRepository.Object,
                                                   _syncJobRepository.Object,
                                                   _syncJobChangeRepository.Object,
                                                   _titlesRepository.Object,
                                                   _settingsRepository.Object,
                                                   _notificationService.Object,
                                                   _thresholdConfig.Object);

            _removeGMMHandler = new RemoveGMMHandler(_loggingRepository.Object,
                                                    _graphGroupRepository.Object,
                                                   _syncJobRepository.Object);

            _getGroupHandler = new GetGroupHandler(_loggingRepository.Object,
                                                            _syncJobRepository.Object,
                                                            _groupRepository.Object,
                                                            _titlesRepository.Object,
                                                            _graphGroupRepository.Object,
                                                            _httpContextAccessor.Object);

            _getChannelHandler = new GetChannelHandler(_loggingRepository.Object,
                                                            _syncJobRepository.Object,
                                                            _channelRepository.Object,
                                                            _titlesRepository.Object,
                                                            _teamsChannelRepository.Object,
                                                            _graphGroupRepository.Object,
                                                            _httpContextAccessor.Object);

            _getJobChangesHandler = new GetJobChangesHandler(_loggingRepository.Object,
                                                            _syncJobChangeRepository.Object);

            _getSyncJobHistoryHandler = new GetSyncJobHistoryHandler(_loggingRepository.Object,
                                                                    _syncJobHistoryRepository.Object);

            _notificationRepository = new Mock<INotificationRepository>();
            _getThresholdNotificationHandlerMock = new Mock<IRequestHandler<GetThresholdNotificationRequest, GetThresholdNotificationResponse>>();
            _getThresholdNotificationHandlerMock
                .Setup(x => x.ExecuteAsync(It.IsAny<GetThresholdNotificationRequest>()))
                .ReturnsAsync(new GetThresholdNotificationResponse { StatusCode = System.Net.HttpStatusCode.NotFound });

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow(Roles.JOB_TENANT_READER)]
        [DataRow("UserRole")]
        public async Task GetJobDetailsForGroupTestAsync(string role)
        {
            var userId = Guid.NewGuid().ToString();
            _jobEntity.DestinationOwners = new List<DestinationOwner>
                {
                    new DestinationOwner
                    {
                        ObjectId = Guid.Parse(userId)
                    }
                };

            var context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            var response = await _jobDetailsController.GetJobDetailsAsync(_jobEntity.Id);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var job = result.Value as SyncJobDetails;

            Assert.IsNotNull(job.StartDate);
            Assert.IsNotNull(job.Requestor);
            Assert.IsNotNull(job.Query);
            Assert.IsNotNull(job.TargetGroupId);
            Assert.IsNotNull(job.TargetGroupName);
            Assert.IsNull(job.TargetChannelId);
            Assert.IsNull(job.TargetChannelName);
            Assert.IsNotNull(job.Titles);
        }

        [TestMethod]
        public async Task GetJobDetailsIncludesHiddenMembershipSourcesAsync()
        {
            var userId = Guid.NewGuid().ToString();
            _jobEntity.DestinationOwners = new List<DestinationOwner>
                {
                    new DestinationOwner
                    {
                        ObjectId = Guid.Parse(userId)
                    }
                };

            var hiddenGroupId = Guid.NewGuid();
            var visibleGroupId = Guid.NewGuid();

            _jobEntity.Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{hiddenGroupId}\"}},{{\"type\":\"GroupMembership\",\"source\":\"{visibleGroupId}\"}}]";

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new List<AzureADGroup>
                {
                    new AzureADGroup { ObjectId = hiddenGroupId, Visibility = "HiddenMembership" },
                    new AzureADGroup { ObjectId = visibleGroupId, Visibility = "Public" }
                });

            var context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.JOB_OWNER_WRITER),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            var response = await _jobDetailsController.GetJobDetailsAsync(_jobEntity.Id);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(result);
            var job = result.Value as SyncJobDetails;

            Assert.IsNotNull(job);
            Assert.IsTrue(job.HasHiddenMembershipSources);
            Assert.IsTrue(job.HiddenMembershipSourceIds.Contains(hiddenGroupId));
            Assert.IsFalse(job.HiddenMembershipSourceIds.Contains(visibleGroupId));
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow(Roles.JOB_TENANT_READER)]
        [DataRow("UserRole")]
        public async Task GetJobDetailsForChannelTestAsync(string role)
        {
            _jobEntity.MembershipType = MembershipTypes.TeamsChannelMembership.ToString();
            _jobEntity.Channel = _channel;
            _teamsChannelRepository.Setup(x => x.GetTeamsChannelNameAsync(It.IsAny<AzureADTeamsChannel>()))
                                    .ReturnsAsync(() => "Channel Name");

            var userId = Guid.NewGuid().ToString();
            _jobEntity.DestinationOwners = new List<DestinationOwner>
                {
                    new DestinationOwner
                    {
                        ObjectId = Guid.Parse(userId)
                    }
                };

            var context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            var response = await _jobDetailsController.GetJobDetailsAsync(_jobEntity.Id);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var job = result.Value as SyncJobDetails;

            Assert.IsNotNull(job.StartDate);
            Assert.IsNotNull(job.Requestor);
            Assert.IsNotNull(job.Query);
            Assert.IsNotNull(job.TargetGroupId);
            Assert.IsNotNull(job.TargetGroupName);
            Assert.IsNotNull(job.TargetChannelId);
            Assert.IsNotNull(job.TargetChannelName);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow(Roles.JOB_TENANT_READER)]
        [DataRow("UserRole")]
        public async Task GetGroupDetailsTestAsync(string role)
        {
            var userId = Guid.NewGuid().ToString();
            _jobEntity.DestinationOwners = new List<DestinationOwner>
                {
                    new DestinationOwner
                    {
                        ObjectId = Guid.Parse(userId)
                    }
                };

            var context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            var response = await _jobDetailsController.GetGroupDetailsAsync(_jobEntity.Group.GroupId);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var job = result.Value as SyncJobDetails;

            Assert.IsNotNull(job.StartDate);
            Assert.IsNotNull(job.Requestor);
            Assert.IsNotNull(job.Query);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow(Roles.JOB_TENANT_READER)]
        [DataRow("UserRole")]
        public async Task GetChannelDetailsTestAsync(string role)
        {
            var userId = Guid.NewGuid().ToString();
            _jobEntity.DestinationOwners = new List<DestinationOwner>
                {
                    new DestinationOwner
                    {
                        ObjectId = Guid.Parse(userId)
                    }
                };

            _jobEntity.Group = null;

            _jobEntity.Channel = new Channel
            {
                GroupId = Guid.NewGuid(),
                ChannelId = "channel1",
                SyncJobId = _jobEntity.Id
            };

            var context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            var response = await _jobDetailsController.GetChannelDetailsAsync(_jobEntity.Channel.GroupId, _jobEntity.Channel.ChannelId);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var job = result.Value as SyncJobDetails;

            Assert.IsNotNull(job.StartDate);
            Assert.IsNotNull(job.Requestor);
            Assert.IsNotNull(job.Query);
        }

        [TestMethod]
        [DataRow(Roles.JOB_TENANT_READER)]
        public async Task GetJobDetailsTestRequestorNotAnOwnerAsync(string role)
        {
            var context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _graphGroupRepository = new Mock<IGraphGroupRepository>();

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>()))
                                    .ReturnsAsync(() => false);

            _getJobDetailsHandler = new GetJobDetailsHandler(
                                     _loggingRepository.Object,
                                     _syncJobRepository.Object,
                                     _syncJobChangeRepository.Object,
                                     _titlesRepository.Object,
                                     _graphGroupRepository.Object,
                                     _teamsChannelRepository.Object,
                                     _httpContextAccessor.Object,
                                     _handleInactiveJobsConfig.Object);

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object);

            var response = await _jobDetailsController.GetJobDetailsAsync(_jobEntity.Id);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var job = result.Value as SyncJobDetails;

            Assert.IsNotNull(job);
            Assert.AreEqual("example@microsoft.com", job.Requestor);
        }
        [TestMethod]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task GetJobDetailsWhenClaimIsNotFound(string role)
        {
            var context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            var response = await _jobDetailsController.GetJobDetailsAsync(Guid.NewGuid());
            var result = response.Result as ForbidResult;

            Assert.IsInstanceOfType(result, typeof(ForbidResult));

            _syncJobRepository.Verify(x => x.GetSyncJobAsync(It.IsAny<Guid>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
            public async Task GetJobDetailsWhenNoJobIsFound(string role)
            {
                var syncJobId = Guid.NewGuid();

            var context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier",  Guid.NewGuid().ToString())
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(context)
            };

            _syncJobRepository.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>()))
                              .ReturnsAsync((SyncJob)null);

            var response = await _jobDetailsController.GetJobDetailsAsync(syncJobId);
            var result = response.Result as NotFoundResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(404, result.StatusCode);

            _syncJobRepository.Verify(x => x.GetSyncJobs(It.IsAny<bool>()), Times.Once);
        }

        [TestMethod]
        [DataRow(Roles.JOB_TENANT_READER)]
        public async Task GetGroupDetailsTestRequestorNotAnOwnerAsync(string role)
        {
            var context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _graphGroupRepository = new Mock<IGraphGroupRepository>();

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>()))
                                    .ReturnsAsync(() => false);

            _getJobDetailsHandler = new GetJobDetailsHandler(
                                     _loggingRepository.Object,
                                     _syncJobRepository.Object,
                                     _syncJobChangeRepository.Object,
                                     _titlesRepository.Object,
                                     _graphGroupRepository.Object,
                                     _teamsChannelRepository.Object,
                                     _httpContextAccessor.Object,
                                     _handleInactiveJobsConfig.Object);

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object);

            var response = await _jobDetailsController.GetGroupDetailsAsync(Guid.NewGuid());
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var job = result.Value as SyncJobDetails;

            Assert.IsNotNull(job);
            Assert.AreEqual("example@microsoft.com", job.Requestor);
        }

        [TestMethod]
        [DataRow(Roles.JOB_TENANT_READER)]
        public async Task GetChannelDetailsTestRequestorNotAnOwnerAsync(string role)
        {
            var context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _graphGroupRepository = new Mock<IGraphGroupRepository>();

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>()))
                                    .ReturnsAsync(() => false);

            _getJobDetailsHandler = new GetJobDetailsHandler(
                                     _loggingRepository.Object,
                                     _syncJobRepository.Object,
                                     _syncJobChangeRepository.Object,
                                     _titlesRepository.Object,
                                     _graphGroupRepository.Object,
                                     _teamsChannelRepository.Object,
                                     _httpContextAccessor.Object,
                                     _handleInactiveJobsConfig.Object);

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object);

            var response = await _jobDetailsController.GetChannelDetailsAsync(Guid.NewGuid(), string.Empty);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var job = result.Value as SyncJobDetails;

            Assert.IsNotNull(job);
            Assert.AreEqual("example@microsoft.com", job.Requestor);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_ENABLER)]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task EnableJobSuccessAsync(string role)
        {
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            var newStatus = SyncStatus.Idle.ToString();
            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Status", Value = ConvertToJsonElement(newStatus) },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.StatusUpdate.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Testing status update") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.StatusUpdate.ToString(), "Testing status update");
            var response = await _jobDetailsController.EnableJobAsync(_jobEntity.Id, requestDTO);
            var result = response as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(200, result.StatusCode);
            Assert.AreEqual(newStatus, _jobEntity.Status);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Once);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_ENABLER)]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task PatchTitlesAsync(string role)
        {
            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAITitleEnabled))
                               .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAITitleEnabled, SettingValue = "true" });

            _jobEntity.Status = SyncStatus.Idle.ToString();

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            var newStatus = SyncStatus.CustomerPaused.ToString();
            var mockTitles = new List<Title>
            {
                new Title
                {
                    PartId = Guid.Parse("550e8400-e29b-41d4-a716-446655440000"),
                    Name = "Software Engineers"
                },
                new Title
                {
                    PartId = Guid.Parse("550e8400-e29b-41d4-a716-446655440001"),
                    Name = "Product Managers"
                }
            };
            var titlesJson = JsonSerializer.Serialize(mockTitles);

            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Status", Value = ConvertToJsonElement(newStatus) },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.StatusUpdate.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Testing titles update") },
                new PatchOperation { Op = "replace", Path = "/Titles", Value = ConvertToJsonElement(titlesJson) }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.StatusUpdate.ToString(), "Testing titles update");
            var response = await _jobDetailsController.EnableJobAsync(_jobEntity.Id, requestDTO);
            var result = response as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(200, result.StatusCode);
            Assert.AreEqual(newStatus, _jobEntity.Status);

            _titlesRepository.Verify(x => x.UpdateTitlesAsync(
                It.Is<List<Title>>(titles =>
                    titles.Count == 2 &&
                    titles.All(t => t.SyncJobId == _jobEntity.Id) &&
                    titles.Any(t => t.Name == "Software Engineers") &&
                    titles.Any(t => t.Name == "Product Managers")),
                _jobEntity.Id), Times.Once);

            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Once);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_ENABLER)]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task PatchNoTitlesAsync(string role)
        {

            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAITitleEnabled))
                                    .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAITitleEnabled, SettingValue = "false" });


            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            var newStatus = SyncStatus.Idle.ToString();
            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Status", Value = ConvertToJsonElement(newStatus) },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.StatusUpdate.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Testing no titles") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.StatusUpdate.ToString(), "Testing no titles");
            var response = await _jobDetailsController.EnableJobAsync(_jobEntity.Id, requestDTO);
            var result = response as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(200, result.StatusCode);
            Assert.AreEqual(newStatus, _jobEntity.Status);
            _titlesRepository.Verify(x => x.UpdateTitlesAsync(It.IsAny<List<Title>>(), It.IsAny<Guid>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_ENABLER)]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task EnableJobToBadStatusAsync(string role)
        {
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            _jobEntity.Status = SyncStatus.CustomerPaused.ToString();

            var newStatus = SyncStatus.Error.ToString();
            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Status", Value = ConvertToJsonElement(newStatus) },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.StatusUpdate.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Testing bad status") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.StatusUpdate.ToString(), "Testing bad status");
            var response = await _jobDetailsController.EnableJobAsync(_jobEntity.Id, requestDTO);
            var result = response as BadRequestObjectResult;
            var patchResponse = result.Value as PatchJobResponse;

            Assert.IsNotNull(result);
            Assert.AreEqual(HttpStatusCode.BadRequest, patchResponse.StatusCode);
            Assert.AreEqual("ValidUpdateStatusIsRequired", patchResponse.ErrorCode);
            Assert.AreEqual(SyncStatus.CustomerPaused.ToString(), _jobEntity.Status);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }


        [TestMethod]
        [DataRow(Roles.JOB_OWNER_ENABLER)]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task EnablePendingReviewJobFailedAsync(string role)
        {
            _jobEntity.Status = SyncStatus.PendingReview.ToString();
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            var newStatus = SyncStatus.Idle.ToString();
            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Status", Value = ConvertToJsonElement(newStatus) },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.StatusUpdate.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Testing pending review") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.StatusUpdate.ToString(), "Testing pending review");
            var response = await _jobDetailsController.EnableJobAsync(_jobEntity.Id, requestDTO);
            var result = response as ObjectResult;
            var problem = result.Value as ProblemDetails;

            Assert.IsNotNull(result);
            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _jobEntity.Status);
            Assert.AreEqual((int)HttpStatusCode.PreconditionFailed, problem.Status);
            Assert.AreEqual("JobInPendingReviewStateCannotBeUpdated", problem.Detail);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.SUBMISSION_REVIEWER)]
        public async Task ApproveSubmissionAsync(string role)
        {
            var userId = Guid.NewGuid().ToString();
            var context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, role),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _graphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync((List<Guid> objectIds) =>
                {
                    return new Dictionary<Guid, List<Guid>>
                    {
                        { Guid.NewGuid(), new List<Guid> { (Guid)_syncJobChange.ChangedByObjectId } }
                    };
                });
            _jobEntity.Status = SyncStatus.PendingReview.ToString();
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Status", Value = ConvertToJsonElement("Idle") },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.SubmissionApproved.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Approved") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.SubmissionApproved.ToString(), "Approved");
            var response = await _jobDetailsController.ReviewJobAsync(_jobEntity.Id, requestDTO);
            var result = response as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.OK, result.StatusCode);
            Assert.AreEqual(SyncStatus.Idle.ToString(), _jobEntity.Status);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Once);
        }

        [TestMethod]
        [DataRow(Roles.SUBMISSION_REVIEWER)]
        public async Task ApproveSubmissionSetsThresholdViolationsAsync(string role)
        {
            var userId = Guid.NewGuid().ToString();
            var context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, role),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _graphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync((List<Guid> objectIds) =>
                {
                    return new Dictionary<Guid, List<Guid>>
                    {
                        { Guid.NewGuid(), new List<Guid> { (Guid)_syncJobChange.ChangedByObjectId } }
                    };
                });
            _jobEntity.Status = SyncStatus.PendingReview.ToString();
            _jobEntity.LastRunTime = DateTime.UtcNow.AddHours(-1);
            _jobEntity.ThresholdViolations = 0;
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };

            var newStatus = SyncStatus.Idle.ToString();
            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Status", Value = ConvertToJsonElement(newStatus) },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.SubmissionApproved.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Approved") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.SubmissionApproved.ToString(), "Approved");
            var response = await _jobDetailsController.ReviewJobAsync(_jobEntity.Id, requestDTO);
            var result = response as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.OK, result.StatusCode);
            Assert.AreEqual(SyncStatus.Idle.ToString(), _jobEntity.Status);
            Assert.AreEqual(2, _jobEntity.ThresholdViolations);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Once);
        }

        [TestMethod]
        [DataRow(Roles.SUBMISSION_REVIEWER)]
        [DataRow(Roles.SUBMISSION_REJECTOR)]
        public async Task RejectSubmissionAsync(string role)
        {
            var userId = Guid.NewGuid().ToString();
            var context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, role),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _graphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync((List<Guid> objectIds) =>
                {
                    return new Dictionary<Guid, List<Guid>>
                    {
                        { Guid.NewGuid(), new List<Guid> { (Guid)_syncJobChange.ChangedByObjectId } }
                    };
                });
            _jobEntity.Status = SyncStatus.PendingReview.ToString();
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Status", Value = ConvertToJsonElement("SubmissionRejected") },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.SubmissionRejected.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Rejected") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.SubmissionRejected.ToString(), "Rejected");
            var response = await _jobDetailsController.ReviewJobAsync(_jobEntity.Id, requestDTO);
            var result = response as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.OK, result.StatusCode);
            Assert.AreEqual(SyncStatus.SubmissionRejected.ToString(), _jobEntity.Status);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Once);
        }

        [TestMethod]
        [DataRow(Roles.SUBMISSION_REJECTOR)]
        public async Task FailSubmissionRejectorApprovalAsync(string role)
        {
            var userId = Guid.NewGuid().ToString();
            var context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, role),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _graphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync((List<Guid> objectIds) =>
                {
                    return new Dictionary<Guid, List<Guid>>
                    {
                        { Guid.NewGuid(), new List<Guid> { (Guid)_syncJobChange.ChangedByObjectId } }
                    };
                });
            _jobEntity.Status = SyncStatus.PendingReview.ToString();
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Status", Value = ConvertToJsonElement("Idle") },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.SubmissionApproved.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Approved attempt by rejector") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.SubmissionApproved.ToString(), "Approved attempt by rejector");
            var response = await _jobDetailsController.ReviewJobAsync(_jobEntity.Id, requestDTO);
            var result = response as BadRequestObjectResult;

            var patchResponse = result.Value as PatchJobResponse;
            Assert.IsNotNull(patchResponse);
            Assert.AreEqual("OnlySubmissionReviewerCanApproveSubmission", patchResponse.ErrorCode);
            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _jobEntity.Status);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.SUBMISSION_REVIEWER)]
        public async Task ReviewOwnSubmissionWithoutPermission(string role)
        {
            var userId = Guid.NewGuid();
            var context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, role),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId.ToString())
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _graphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync((List<Guid> objectIds) =>
                {
                    return new Dictionary<Guid, List<Guid>>
                    {
                        { Guid.NewGuid(), new List<Guid> { userId } }
                    };
                });
            _jobEntity.Status = SyncStatus.PendingReview.ToString();

            _settingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.CanReviewOwnSubmissions))
                .ReturnsAsync(() => new Setting { SettingKey = SettingKey.CanReviewOwnSubmissions, SettingValue = "false" });

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId.ToString())})
            };

            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Status", Value = ConvertToJsonElement("Idle") },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.SubmissionApproved.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Own submission review attempt") }
            };

            var syncJobChangeByUserId = new SyncJobChange
            {
                ChangedByObjectId = userId,
                ChangeReason = SyncJobChangeReason.Update.ToString()
            };
            _syncJobChangeRepository.Setup(x => x.GetLastSyncJobChangeBySyncJobIdAsync(It.IsAny<Guid>()))
                                    .ReturnsAsync(() => syncJobChangeByUserId);

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.SubmissionApproved.ToString(), "Own submission review attempt");
            var response = await _jobDetailsController.ReviewJobAsync(_jobEntity.Id, requestDTO);
            var result = response as BadRequestObjectResult;
            var patchResponse = result.Value as PatchJobResponse;
            Assert.IsNotNull(patchResponse);
            Assert.AreEqual("ReviewerCannotReviewOwnSubmission", patchResponse.ErrorCode);
            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _jobEntity.Status);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.SUBMISSION_REVIEWER)]
        public async Task ReviewSubmissionWhereSubmitterIsNotOwner(string role)
        {
            var userId = Guid.NewGuid().ToString();
            var context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, role),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _graphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync((List<Guid> objectIds) => null);
            _jobEntity.Status = SyncStatus.PendingReview.ToString();
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Status", Value = ConvertToJsonElement("Idle") },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.SubmissionApproved.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Submitter not owner") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.SubmissionApproved.ToString(), "Submitter not owner");
            var response = await _jobDetailsController.ReviewJobAsync(_jobEntity.Id, requestDTO);
            var result = response as BadRequestObjectResult;
            var patchResponse = result.Value as PatchJobResponse;

            Assert.IsNotNull(result);
            Assert.AreEqual(HttpStatusCode.BadRequest, patchResponse.StatusCode);
            Assert.AreEqual("SubmitterNotOwner", patchResponse.ErrorCode);
            Assert.AreEqual(SyncStatus.SubmissionRejected.ToString(), _jobEntity.Status);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Once);
        }

        [TestMethod]
        [DataRow(Roles.SUBMISSION_REVIEWER)]
        public async Task ReviewSubmissionWithBadEndStatusAsync(string role)
        {
            var userId = Guid.NewGuid().ToString();
            var context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, role),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _graphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync((List<Guid> objectIds) =>
                {
                    return new Dictionary<Guid, List<Guid>>
                    {
                        { Guid.NewGuid(), new List<Guid> { (Guid)_syncJobChange.ChangedByObjectId } }
                    };
                });
            _jobEntity.Status = SyncStatus.PendingReview.ToString();
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Status", Value = ConvertToJsonElement("Error") },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.SubmissionApproved.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Bad end status") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.SubmissionApproved.ToString(), "Bad end status");
            var response = await _jobDetailsController.ReviewJobAsync(_jobEntity.Id, requestDTO);
            var result = response as BadRequestObjectResult;
            var patchResponse = result.Value as PatchJobResponse;

            Assert.IsNotNull(result);
            Assert.AreEqual(HttpStatusCode.BadRequest, patchResponse.StatusCode);
            Assert.AreEqual("ValidUpdateStatusIsRequired", patchResponse.ErrorCode);
            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _jobEntity.Status);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task UpdateJobSuccessAsync(string role)
        {
            _jobEntity.Status = SyncStatus.Idle.ToString();
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            var newQuery = "UpdatedQuery";
            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Query", Value = ConvertToJsonElement(newQuery) },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.Update.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Updating query") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.Update.ToString(), "Updating query");
            var response = await _jobDetailsController.UpdateJobAsync(_jobEntity.Id, requestDTO);
            var result = response as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(200, result.StatusCode);
            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _jobEntity.Status);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Once);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task UpdatePendingReviewJobFailureAsync(string role)
        {
            _jobEntity.Status = SyncStatus.PendingReview.ToString();
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Query", Value = ConvertToJsonElement("UpdatedQuery") },
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.Update.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Update pending review") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.Update.ToString(), "Update pending review");
            var response = await _jobDetailsController.UpdateJobAsync(_jobEntity.Id, requestDTO);
            var result = response as ObjectResult;
            var problem = result.Value as ProblemDetails;

            Assert.IsNotNull(result);
            Assert.AreEqual(SyncStatus.PendingReview.ToString(), _jobEntity.Status);
            Assert.AreEqual((int)HttpStatusCode.PreconditionFailed, problem.Status);
            Assert.AreEqual("JobInPendingReviewStateCannotBeUpdated", problem.Detail);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        public async Task PatchJobWhenChangeReasonIsEmpty(string role)
        {
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/Status", Value = ConvertToJsonElement("InvalidStatus") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, null, "");
            var response = await _jobDetailsController.UpdateJobAsync(_jobEntity.Id, requestDTO);
            var result = response as BadRequestObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(400, result.StatusCode);
            var responseObject = result.Value as PatchJobResponse;
            Assert.IsNotNull(responseObject);
            Assert.AreEqual("ChangeReasonIsRequired", responseObject.ErrorCode);

            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        public async Task PatchJobWhenSyncJobDoesNotExist(string role)
        {
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            _syncJobRepository.Setup(x => x.GetSyncJobAsync(_jobEntity.Id))
                              .ReturnsAsync(() => null);

            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.Update.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Test") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.Update.ToString(), "Test");
            var response = await _jobDetailsController.UpdateJobAsync(_jobEntity.Id, requestDTO);
            var result = response as NotFoundObjectResult;
            var patchResponse = result.Value as PatchJobResponse;

            Assert.IsNotNull(result);
            Assert.AreEqual(SyncStatus.Idle.ToString(), _jobEntity.Status);
            Assert.AreEqual(HttpStatusCode.NotFound, patchResponse.StatusCode);

            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        public async Task PatchJobWhenGroupIdNull(string role)
        {
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            _jobEntity.Group = null;

            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.Update.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Test") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.Update.ToString(), "Test");
            var response = await _jobDetailsController.UpdateJobAsync(_jobEntity.Id, requestDTO);
            var result = response as BadRequestObjectResult;
            var patchResponse = result.Value as PatchJobResponse;

            Assert.IsNotNull(result);
            Assert.AreEqual(SyncStatus.Idle.ToString(), _jobEntity.Status);
            Assert.AreEqual(HttpStatusCode.BadRequest, patchResponse.StatusCode);
            Assert.AreEqual("GroupIdNotFound", patchResponse.ErrorCode);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        public async Task PatchJobWhenNotGroupOwner(string role)
        {
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>()))
                                    .ReturnsAsync(() => false);

            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.Update.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Test") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.Update.ToString(), "Test");
            var response = await _jobDetailsController.UpdateJobAsync(_jobEntity.Id, requestDTO);
            var result = response as ForbidResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(SyncStatus.Idle.ToString(), _jobEntity.Status);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        public async Task PatchJobWhileJobInProgress(string role)
        {
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            _jobEntity.Status = SyncStatus.InProgress.ToString();

            var operations = new List<PatchOperation>
            {
                new PatchOperation { Op = "replace", Path = "/ChangeReason", Value = ConvertToJsonElement(SyncJobChangeReason.Update.ToString()) },
                new PatchOperation { Op = "replace", Path = "/BusinessJustification", Value = ConvertToJsonElement("Test") }
            };

            var requestDTO = CreatePatchJobRequestDTO(operations, SyncJobChangeReason.Update.ToString(), "Test");
            var response = await _jobDetailsController.UpdateJobAsync(_jobEntity.Id, requestDTO);
            var result = response as ObjectResult;
            var problem = result.Value as ProblemDetails;

            Assert.IsNotNull(result);
            Assert.AreEqual(SyncStatus.InProgress.ToString(), _jobEntity.Status);
            Assert.AreEqual((int)HttpStatusCode.PreconditionFailed, problem.Status);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_DELETER)]
        public async Task RemoveGMMAsyncWhenIsAnAuthorizedUser(string role)
        {
            var context = CreateHttpContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())});


            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(context)
            };

            var response = await _jobDetailsController.RemoveGMMAsync(_jobEntity.Id);
            var result = response as OkResult;

            Assert.IsInstanceOfType(result, typeof(OkResult));
        }

        [TestMethod]
        [DataRow(Roles.HYPERLINK_ADMINISTRATOR)]
        public async Task RemoveGMMAsyncWhenIsAnUnauthorizedUser(string role)
        {
            var userId = Guid.NewGuid().ToString();

            var context = CreateHttpContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)});


            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(context)
            };

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(userId, _jobEntity.Group.GroupId, It.IsAny<bool>()))
                                    .ReturnsAsync(() => false);

            var response = await _jobDetailsController.RemoveGMMAsync(_jobEntity.Id);
            var result = response as ForbidResult;

            Assert.IsInstanceOfType(result, typeof(ForbidResult));
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        public async Task RemoveGMMAsyncWhenInvalidGroup(string role)
        {
            var syncJobId = Guid.NewGuid();

            var context = CreateHttpContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "notOwner@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())});


            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(context)
            };

            _syncJobRepository.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>()))
                              .ReturnsAsync((SyncJob)null);

            var response = await _jobDetailsController.RemoveGMMAsync(syncJobId);
            var result = response as NotFoundResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(404, result.StatusCode);
        }

        [TestMethod]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task RemoveGMMAsyncWhenIsClaimIsNotFound(string role)
        {
            var syncJobId = Guid.NewGuid();

            var context = CreateHttpContext(new List<Claim> {
                        new Claim(ClaimTypes.Name, "user@domain.com"),
                        new Claim(ClaimTypes.Role, role)
                    });

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object)
            {
                ControllerContext = CreateControllerContext(context)
            };

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<bool>()))
                                    .ReturnsAsync(() => false);

            var response = await _jobDetailsController.RemoveGMMAsync(syncJobId);
            var result = response as ForbidResult;

            Assert.IsInstanceOfType(result, typeof(ForbidResult));

            _syncJobRepository.Verify(x => x.DeleteSyncJobAsync(It.IsAny<SyncJob>()), Times.Never);
        }

        [TestMethod]
        public async Task RemoveGMMThrowsExceptionReturnsInternalServerError()
        {
            var userId = Guid.NewGuid().ToString();
            var context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_OWNER_DELETER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _removeGMMHandler = new RemoveGMMHandler(_loggingRepository.Object, _graphGroupRepository.Object, _syncJobRepository.Object);
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler, _getSyncJobHistoryHandler, _getMembershipDownloadHandler, _getThresholdNotificationHandlerMock.Object);

            _syncJobRepository.Setup(x => x.DeleteSyncJobAsync(It.IsAny<SyncJob>())).ThrowsAsync(new Exception());
            var response = await _jobDetailsController.RemoveGMMAsync(Guid.NewGuid());
            var result = response as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(500, result.StatusCode);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow(Roles.JOB_TENANT_READER)]
        [DataRow("UserRole")]
        public async Task GetJobChangesTestAsync(string role)
        {
            var userId = Guid.NewGuid().ToString();
            _jobEntity.DestinationOwners = new List<DestinationOwner>
                {
                    new DestinationOwner
                    {
                        ObjectId = Guid.Parse(userId)
                    }
                };

            var context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            var response = await _jobDetailsController.GetJobChangesAsync(_jobEntity.Id);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.OK, result.StatusCode);
            var responseChanges = result.Value as RepositoryPage<SyncJobChange>;
            Assert.IsNotNull(responseChanges);
            Assert.AreEqual(1, responseChanges.TotalCount);
            Assert.AreEqual(1, responseChanges.Items.Count());
            Assert.AreEqual("Test", responseChanges.Items.First().ChangedByDisplayName);
        }

        [TestMethod]
        [DataRow(Roles.JOB_TENANT_READER)]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task GetSyncJobHistoryTestAsync(string role)
        {
            var userId = Guid.NewGuid().ToString();

            var context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
                });

            _jobDetailsController.ControllerContext = CreateControllerContext(context);
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            var response = await _jobDetailsController.GetSyncJobHistoryAsync(_jobEntity.Id);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.OK, result.StatusCode);

            var history = result.Value as List<SyncJobHistory>;
            Assert.IsNotNull(history);
            Assert.AreEqual(1, history.Count);
            Assert.AreEqual(_jobEntity.Id, history[0].SyncJobId);
            Assert.AreEqual(SyncStatus.Idle.ToString(), history[0].Status);
        }

        [TestMethod]
        public async Task GetSyncJobHistory_ExcludesInProgressRecords()
        {
            // Arrange
            var userId = Guid.NewGuid().ToString();
            var completedHistories = new List<SyncJobHistory>
            {
                new SyncJobHistory
                {
                    Id = Guid.NewGuid(),
                    SyncJobId = _jobEntity.Id,
                    RunId = Guid.NewGuid(),
                    StartTime = DateTime.UtcNow.AddMinutes(-30),
                    EndTime = DateTime.UtcNow.AddMinutes(-15),
                    Duration = 900,
                    Status = SyncStatus.Idle.ToString(),
                    UsersAdded = 5,
                    UsersRemoved = 2,
                    UpdatedByFunction = "GraphUpdater",
                    CreatedAt = DateTime.UtcNow.AddMinutes(-30),
                    UpdatedAt = DateTime.UtcNow.AddMinutes(-15)
                },
                new SyncJobHistory
                {
                    Id = Guid.NewGuid(),
                    SyncJobId = _jobEntity.Id,
                    RunId = Guid.NewGuid(),
                    StartTime = DateTime.UtcNow.AddHours(-2),
                    EndTime = DateTime.UtcNow.AddHours(-1),
                    Duration = 3600,
                    Status = SyncStatus.Error.ToString(),
                    UsersAdded = 0,
                    UsersRemoved = 0,
                    UpdatedByFunction = "GraphUpdater",
                    CreatedAt = DateTime.UtcNow.AddHours(-2),
                    UpdatedAt = DateTime.UtcNow.AddHours(-1)
                }
            };

            _syncJobHistoryRepository.Setup(x => x.GetBySyncJobIdAsync(_jobEntity.Id, It.IsAny<int>(), It.IsAny<int>()))
                                     .ReturnsAsync(completedHistories);

            var context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
                });

            _jobDetailsController.ControllerContext = CreateControllerContext(context);

            // Act
            var response = await _jobDetailsController.GetSyncJobHistoryAsync(_jobEntity.Id);
            var result = response.Result as OkObjectResult;

            // Assert
            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.OK, result.StatusCode);

            var history = result.Value as List<SyncJobHistory>;
            Assert.IsNotNull(history);
            Assert.AreEqual(2, history.Count);
            
            // Verify no InProgress status in returned records
            Assert.IsFalse(history.Any(h => h.Status == SyncStatus.InProgress.ToString()), 
                "History should not contain any InProgress records");
            
            // Verify only Idle and Error statuses are returned
            Assert.IsTrue(history.All(h => h.Status == SyncStatus.Idle.ToString() || 
                                          h.Status == SyncStatus.Error.ToString()),
                "History should only contain Idle and Error sync records");
        }

        [TestMethod]
        public async Task SearchSyncJobHistoryByUser_ReturnsOk_WhenHandlerReturnsOk()
        {
            var syncJobId = Guid.NewGuid();
            var userObjectId = Guid.NewGuid();
            var requestId = Guid.NewGuid().ToString();

            var expectedResponse = new SearchSyncHistoryByUserResponse
            {
                StatusCode = HttpStatusCode.OK,
                MatchingRunIds = new List<Guid> { Guid.NewGuid() },
                CheckedCurrentGroupMembership = false,
                UserInCurrentGroup = false
            };

            var searchHandlerMock = new Mock<IRequestHandler<SearchSyncHistoryByUserRequest, SearchSyncHistoryByUserResponse>>();
            searchHandlerMock
                .Setup(x => x.ExecuteAsync(It.IsAny<SearchSyncHistoryByUserRequest>()))
                .ReturnsAsync(expectedResponse);

            var services = new ServiceCollection();
            services.AddSingleton(searchHandlerMock.Object);
            var provider = services.BuildServiceProvider();

            var context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });
            context.RequestServices = provider;
            _jobDetailsController.ControllerContext = CreateControllerContext(context);

            var response = await _jobDetailsController.SearchSyncJobHistoryByUserAsync(syncJobId, userObjectId, requestId);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.OK, result.StatusCode);
            Assert.AreSame(expectedResponse, result.Value);

            searchHandlerMock.Verify(x => x.ExecuteAsync(It.Is<SearchSyncHistoryByUserRequest>(r =>
                r.SyncJobId == syncJobId &&
                r.UserObjectId == userObjectId &&
                r.RequestId == requestId)), Times.Once);
        }

        [TestMethod]
        public async Task SearchSyncJobHistoryByUser_ReturnsNotFound_WhenHandlerReturnsNotFound()
        {
            var syncJobId = Guid.NewGuid();
            var userObjectId = Guid.NewGuid();

            var searchHandlerMock = new Mock<IRequestHandler<SearchSyncHistoryByUserRequest, SearchSyncHistoryByUserResponse>>();
            searchHandlerMock
                .Setup(x => x.ExecuteAsync(It.IsAny<SearchSyncHistoryByUserRequest>()))
                .ReturnsAsync(new SearchSyncHistoryByUserResponse
                {
                    StatusCode = HttpStatusCode.NotFound
                });

            var services = new ServiceCollection();
            services.AddSingleton(searchHandlerMock.Object);
            var provider = services.BuildServiceProvider();

            var context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });
            context.RequestServices = provider;
            _jobDetailsController.ControllerContext = CreateControllerContext(context);

            var response = await _jobDetailsController.SearchSyncJobHistoryByUserAsync(syncJobId, userObjectId);
            var result = response.Result as NotFoundResult;

            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.NotFound, result.StatusCode);
        }

        [TestMethod]
        public async Task GetThresholdNotification_ReturnsOk_WhenNotificationExists()
        {
            var notificationId = Guid.NewGuid();
            _getThresholdNotificationHandlerMock
                .Setup(x => x.ExecuteAsync(It.IsAny<GetThresholdNotificationRequest>()))
                .ReturnsAsync(new GetThresholdNotificationResponse
                {
                    StatusCode = HttpStatusCode.OK,
                    NotificationId = notificationId,
                    ChangeQuantityForAdditions = 98,
                    ChangePercentageForAdditions = 4900.0,
                    ThresholdPercentageForAdditions = 10,
                    ChangeQuantityForRemovals = 0,
                    ChangePercentageForRemovals = 0.0,
                    ThresholdPercentageForRemovals = 10
                });

            var result = await _jobDetailsController.GetThresholdNotificationAsync(_jobEntity.Id) as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.OK, result.StatusCode);

            var response = result.Value as GetThresholdNotificationResponse;
            Assert.IsNotNull(response);
            Assert.AreEqual(notificationId, response.NotificationId);
        }

        [TestMethod]
        public async Task GetThresholdNotification_ReturnsNotFound_WhenNotificationDoesNotExist()
        {
            _getThresholdNotificationHandlerMock
                .Setup(x => x.ExecuteAsync(It.IsAny<GetThresholdNotificationRequest>()))
                .ReturnsAsync(new GetThresholdNotificationResponse { StatusCode = HttpStatusCode.NotFound });

            var result = await _jobDetailsController.GetThresholdNotificationAsync(_jobEntity.Id) as NotFoundResult;

            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.NotFound, result.StatusCode);
        }

        [TestMethod]
        public async Task GetThresholdNotification_ReturnsInternalServerError_OnException()
        {
            _getThresholdNotificationHandlerMock
                .Setup(x => x.ExecuteAsync(It.IsAny<GetThresholdNotificationRequest>()))
                .ReturnsAsync(new GetThresholdNotificationResponse { StatusCode = HttpStatusCode.InternalServerError });

            var result = await _jobDetailsController.GetThresholdNotificationAsync(_jobEntity.Id) as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual((int)HttpStatusCode.InternalServerError, result.StatusCode);
        }

        private ControllerContext CreateControllerContext(HttpContext httpContext)
        {
            return new ControllerContext { HttpContext = httpContext };
        }

        private ControllerContext CreateControllerContext(List<Claim> claims)
        {
            return new ControllerContext { HttpContext = CreateHttpContext(claims) };
        }

        private HttpContext CreateHttpContext(List<Claim> claims)
        {
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var principal = new ClaimsPrincipal(identity);
            var httpContext = new DefaultHttpContext();
            httpContext.User = principal;

            return httpContext;
        }
    }
}


