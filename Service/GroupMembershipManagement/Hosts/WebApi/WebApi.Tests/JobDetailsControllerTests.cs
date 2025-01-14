// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using MockQueryable.Moq;
using Models;
using Models.SyncJobChange;
using Moq;
using Repositories.Contracts;
using Services.WebApi;
using System.Data;
using System.Net;
using System.Security.Claims;
using WebApi.Controllers.v1.Jobs;
using WebApi.Models.DTOs;
using Roles = WebApi.Models.Roles;
using SyncJob = Models.SyncJob;
using SyncJobDetails = WebApi.Models.DTOs.SyncJobDetails;

namespace Services.Tests
{
    [TestClass]
    public class JobDetailsControllerTests
    {
        private SyncJob _jobEntity = null!;
        private Group _group = null!;
        private Channel _channel = null!;
        private SyncJobChange _syncJobChange = null!;
        private JobDetailsController _jobDetailsController = null!;
        private GetJobDetailsHandler _getJobDetailsHandler = null!;
        private GetGroupHandler _getGroupHandler = null!;
        private GetChannelHandler _getChannelHandler = null!;
        private PatchJobHandler _patchJobHandler = null!;
        private GetJobChangesHandler _getJobChangesHandler = null!;
        private RemoveGMMHandler _removeGMMHandler = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private Mock<IDatabaseGroupsRepository> _groupRepository = null!;
        private Mock<IDatabaseChannelsRepository> _channelRepository = null!;
        private Mock<ISyncJobChangeRepository> _syncJobChangeRepository = null!;
        private Mock<IDatabaseSettingsRepository> _settingsRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private bool _isGroupOwner = true;
        private Mock<IHttpContextAccessor> _httpContextAccessor = null!;

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
            _settingsRepository = new Mock<IDatabaseSettingsRepository>();

            _graphGroupRepository = new Mock<IGraphGroupRepository>();

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                                    .ReturnsAsync(() => _isGroupOwner);

            _jobEntity = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = SyncStatus.Idle.ToString(),
                Destination = $"[{{\"type\":\"GroupMembership\",\"value\":{{\"objectId\":\"{Guid.NewGuid()}\"}}}}]",
                TargetOfficeGroupId = Guid.NewGuid(),
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

            _syncJobChangeRepository.Setup(x => x.GetPageBySyncJobId(_jobEntity.Id, 1, 10, SyncJobChangeSortingField.ChangeTime, false))
                            .ReturnsAsync(changes);

            _syncJobChangeRepository.Setup(x => x.GetLastSyncJobChangeBySyncJobIdAsync(It.IsAny<Guid>()))
                                    .ReturnsAsync(() => _syncJobChange);

            _groupRepository.Setup(x => x.GetGroupAsync(It.IsAny<Guid>()))
                              .ReturnsAsync(() => _group);

            _channelRepository.Setup(x => x.GetChannelAsync(It.IsAny<Guid>(), It.IsAny<string>()))
                             .ReturnsAsync(() => _channel);

            _syncJobRepository.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>()))
                              .ReturnsAsync(() => _jobEntity);

            _syncJobRepository.Setup(x => x.GetSyncJobs(It.IsAny<bool>()))
                              .Returns(() =>
                              {
                                  var jobs = new List<SyncJob> { _jobEntity };
                                  return jobs.BuildMock();
                              });

            _syncJobRepository.Setup(x => x.DeleteSyncJobAsync(It.IsAny<SyncJob>()));

            _getJobDetailsHandler = new GetJobDetailsHandler(_loggingRepository.Object,
                                                             _syncJobRepository.Object,
                                                             _graphGroupRepository.Object,
                                                             _httpContextAccessor.Object);

            _patchJobHandler = new PatchJobHandler(_loggingRepository.Object,
                                                   _graphGroupRepository.Object,
                                                   _syncJobRepository.Object,
                                                   _syncJobChangeRepository.Object,
                                                   _settingsRepository.Object);

            _removeGMMHandler = new RemoveGMMHandler(_loggingRepository.Object,
                                                    _graphGroupRepository.Object,
                                                   _syncJobRepository.Object);

            _getGroupHandler = new GetGroupHandler(_loggingRepository.Object,
                                                            _syncJobRepository.Object,
                                                            _groupRepository.Object,
                                                            _graphGroupRepository.Object,
                                                            _httpContextAccessor.Object);

            _getChannelHandler = new GetChannelHandler(_loggingRepository.Object,
                                                            _syncJobRepository.Object,
                                                            _channelRepository.Object,
                                                            _graphGroupRepository.Object,
                                                            _httpContextAccessor.Object);

            _getJobChangesHandler = new GetJobChangesHandler(_loggingRepository.Object,
                                                            _syncJobChangeRepository.Object);

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler);
        }

        private async IAsyncEnumerable<T> GetItemsAsync<T>(List<T> list)
        {
            foreach (var item in list)
            {
                yield return item;
            }

            await Task.CompletedTask;
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow(Roles.JOB_TENANT_READER)]
        [DataRow("UserRole")]
        public async Task GetJobDetailsTestAsync(string role)
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

            var response = await _jobDetailsController.GetGroupDetailsAsync(_jobEntity.TargetOfficeGroupId);
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

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                                    .ReturnsAsync(() => false);

            _getJobDetailsHandler = new GetJobDetailsHandler(
                                     _loggingRepository.Object,
                                     _syncJobRepository.Object,
                                     _graphGroupRepository.Object,
                                     _httpContextAccessor.Object);

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler);

            var response = await _jobDetailsController.GetJobDetailsAsync(Guid.NewGuid());
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

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler)
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

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                                    .ReturnsAsync(() => false);

            _getJobDetailsHandler = new GetJobDetailsHandler(
                                     _loggingRepository.Object,
                                     _syncJobRepository.Object,
                                     _graphGroupRepository.Object,
                                     _httpContextAccessor.Object);

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler);

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

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                                    .ReturnsAsync(() => false);

            _getJobDetailsHandler = new GetJobDetailsHandler(
                                     _loggingRepository.Object,
                                     _syncJobRepository.Object,
                                     _graphGroupRepository.Object,
                                     _httpContextAccessor.Object);

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler);

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
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task PatchJobWithInvalidStatus(string role)
        {
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())},
                    SyncJobChangeReason.StatusUpdate.ToString())
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, "InvalidStatus");

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as BadRequestObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(400, result.StatusCode);
            Assert.AreEqual("StatusIsNotValid", result.Value);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task PatchJobWithEmptyStatus(string role)
        {
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())},
                    SyncJobChangeReason.StatusUpdate.ToString())
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, null);

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as BadRequestObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(400, result.StatusCode);
            Assert.AreEqual("StatusIsRequired", result.Value);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task PatchJobStatusWhenJobIsInProgress(string role)
        {
            _jobEntity.Status = SyncStatus.InProgress.ToString();

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler)
            {
                ControllerContext = CreateControllerContext(
                    new List<Claim> {
                        new Claim(ClaimTypes.Name, "user@domain.com"),
                        new Claim(ClaimTypes.Role, role),
                        new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                    },
                    "Testing status change"
                )
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, "Idle");

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(412, result.StatusCode);

            var details = result.Value as ProblemDetails;

            Assert.IsNotNull(details);
            Assert.AreEqual("JobInProgress", details.Detail);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task PatchNonExistentJob(string role)
        {
            _jobEntity = null;

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler,_getJobChangesHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())})
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, "Idle");

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as NotFoundResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(404, result.StatusCode);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        public async Task PatchJobWhenIsNotOwnerOfTheGroup(string role)
        {
            _isGroupOwner = false;
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())},
                    SyncJobChangeReason.Update.ToString())
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, "CustomerPaused");

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as ForbidResult;

            Assert.IsNotNull(result);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_TENANT_WRITER)]
        public async Task PatchJobWhenIsNotOwnerOfTheGroupButIsJobTenantWriter(string role)
        {
            _isGroupOwner = false;
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())},
                    SyncJobChangeReason.Update.ToString())
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, "CustomerPaused");

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as OkResult;

            Assert.IsNotNull(result);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Once);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        public async Task PatchJobWhenIsAnOwner(string role)
        {
            var userId = Guid.NewGuid().ToString();
            _jobEntity.DestinationOwners = new List<DestinationOwner>
                {
                    new DestinationOwner
                    {
                        ObjectId = Guid.Parse(userId)
                    }
                };

            var context = CreateHttpContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())},
                    SyncJobChangeReason.Update.ToString());

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler)
            {
                ControllerContext = CreateControllerContext(context)
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, "CustomerPaused");

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as OkResult;

            Assert.IsNotNull(result);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Once);
        }

        [TestMethod]
        public async Task PatchJobThrowsExceptionReturnsInternalServerError()
        {
            var userId = Guid.NewGuid().ToString();
            var context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);
            _syncJobRepository.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception());

            _patchJobHandler = new PatchJobHandler(_loggingRepository.Object,
                                                   _graphGroupRepository.Object,
                                                   _syncJobRepository.Object,
                                                   _syncJobChangeRepository.Object,
                                                   _settingsRepository.Object);
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler,_getJobChangesHandler);

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, "Idle");

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(500, result.StatusCode);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        public async Task PatchJobWhenChangeReasonIsEmpty(string role)
        {
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())},
                    null)
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, "InvalidStatus");

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as BadRequestObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(400, result.StatusCode);
            Assert.AreEqual("ChangeReasonIsRequired", result.Value);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        [DataRow(Roles.JOB_OWNER_DELETER)]
        public async Task RemoveGMMAsyncWhenIsAnAuthorizedUser(string role)
        {
            var syncJobId = Guid.NewGuid();

            var context = CreateHttpContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())});


            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler)
            {
                ControllerContext = CreateControllerContext(context)
            };

            var response = await _jobDetailsController.RemoveGMMAsync(syncJobId);
            var result = response as OkResult;

            Assert.IsInstanceOfType(result, typeof(OkResult));
        }

        [TestMethod]
        [DataRow(Roles.HYPERLINK_ADMINISTRATOR)]
        public async Task RemoveGMMAsyncWhenIsAnUnauthorizedUser(string role)
        {
            var syncJobId = Guid.NewGuid();

            var context = CreateHttpContext(new List<Claim> {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())});


            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler)
            {
                ControllerContext = CreateControllerContext(context)
            };

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                                    .ReturnsAsync(() => false);

            var response = await _jobDetailsController.RemoveGMMAsync(syncJobId);
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


            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler)
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

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler, _getJobChangesHandler)
            {
                ControllerContext = CreateControllerContext(context)
            };

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
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
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _removeGMMHandler, _patchJobHandler, _getGroupHandler, _getChannelHandler,_getJobChangesHandler);

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

        private ControllerContext CreateControllerContext(HttpContext httpContext)
        {
            return new ControllerContext { HttpContext = httpContext };
        }

        private ControllerContext CreateControllerContext(List<Claim> claims, string changeReason = null)
        {
            return new ControllerContext { HttpContext = CreateHttpContext(claims, changeReason) };
        }

        private HttpContext CreateHttpContext(List<Claim> claims, string changeReason = null)
        {
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var principal = new ClaimsPrincipal(identity);
            var httpContext = new DefaultHttpContext();
            httpContext.User = principal;

            if (!string.IsNullOrEmpty(changeReason))
            {
                httpContext.Request.Headers["X-Change-Reason"] = changeReason;
            }

            return httpContext;
        }
    }
}

