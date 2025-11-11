// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using Microsoft.OData.ModelBuilder;
using Microsoft.OData.UriParser;
using Models;
using Models.ServiceBus;
using Models.SyncJobChange;
using Moq;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using Repositories.TeamsChannel;
using Services.Messages.Requests;
using Services.Messages.Responses;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Data;
using System.Net;
using System.Security.Claims;
using WebApi.Controllers.v1.Jobs;
using WebApi.Models;
using WebApi.Models.Responses;
using NewTitle = WebApi.Models.DTOs.NewTitle;
using NewSyncJobDTO = WebApi.Models.DTOs.NewSyncJob;
using PagedResponseDTO = WebApi.Models.DTOs.PagedResponse<WebApi.Models.DTOs.SyncJob>;

namespace Services.Tests
{
    [TestClass]
    public class JobsControllerTests
    {
        private int _jobCount = 1000;
        private NewSyncJobDTO _newSyncJob = null!;
        private HttpContext _context = null!;
        private List<string> _groupTypes = null!;
        private List<SyncJob> _jobEntities = null!;
        private List<string> _syncJobIds = null!;
        private List<AzureADGroup> _groups = null!;
        private JobsController _jobsController = null!;
        private GetJobsHandler _getJobsHandler = null!;
        private PatchJobsHandler _patchJobsHandler = null!;
        private PostJobHandler _postJobHandler = null!;
        private GetJobDetailsHandler _getJobDetailsHandler = null!;
        private TelemetryClient _telemetryClient = null!;
        private Mock<IRequestAdapter> _requestAdapter = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _databaseSyncJobsRepository = null!;
        private Mock<ISyncJobChangeRepository> _syncJobChangeRepository = null!;
        private Mock<IDatabaseTitlesRepository> _titlesRepository = null!;
        private Mock<IDatabaseDestinationAttributesRepository> _destinationAttributesRepository = null!;
        private Mock<GraphServiceClient> _graphServiceClient = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private Mock<ITeamsChannelRepository> _teamsChannelRepository = null!;
        private Mock<IDatabaseSettingsRepository> _databaseSettingsRepository = null!;
        private Mock<IPendingConfigurationConfig> _pendingConfigurationConfig = null!;
        private Mock<IServiceBusQueueRepository> _serviceBusQueueRepository = null!;
        private ODataQueryOptions<SyncJob> _odataQueryOptions = null!;
        private Mock<IHttpContextAccessor> _httpContextAccessor = null!;
        private PostOperationHandler _postResetRequestHandler = null!;
        private Mock<IServiceStatusRepository> _serviceStatusRepository = null!;
        private Mock<IOperationsTaskQueue> _backgroundTaskService = null!;
        private Mock<IThresholdConfig> _thresholdConfig = null!;

        [TestInitialize]
        public void Initialize()
        {
            _groups = new List<AzureADGroup>();
            _context = new DefaultHttpContext();
            _requestAdapter = new Mock<IRequestAdapter>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _databaseSyncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            _syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            _titlesRepository = new Mock<IDatabaseTitlesRepository>();
            _destinationAttributesRepository = new Mock<IDatabaseDestinationAttributesRepository>();
            _httpContextAccessor = new Mock<IHttpContextAccessor>();
            _pendingConfigurationConfig = new Mock<IPendingConfigurationConfig>();
            _serviceBusQueueRepository = new Mock<IServiceBusQueueRepository>();
            _thresholdConfig = new Mock<IThresholdConfig>();

            // Setup default pending configuration setting to false
            _pendingConfigurationConfig.Setup(x => x.PendingConfigurationIsEnabled).Returns(false);
            
            // Setup default threshold config
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToNotify).Returns(3);
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsFollowUps).Returns(3);
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToDisableJob).Returns(10);
            _thresholdConfig.Setup(x => x.MaximumNumberOfThresholdRecipients).Returns(10);

            var builder = new ODataConventionModelBuilder();
            builder.EntitySet<SyncJob>("SyncJob");
            var edmModel = builder.GetEdmModel();

            var odataContext = new ODataQueryContext(edmModel, typeof(SyncJob), new ODataPath());
            _odataQueryOptions = new ODataQueryOptions<SyncJob>(odataContext, _context.Request);


            _requestAdapter.SetupProperty(x => x.BaseUrl).SetReturnsDefault("https://graph.microsoft.com/v1.0");

            _graphServiceClient = new Mock<GraphServiceClient>(_requestAdapter.Object,
                                                               "https://graph.microsoft.com/v1.0");

            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _databaseSettingsRepository = new Mock<IDatabaseSettingsRepository>();

            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAITitleEnabled))
                                     .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAITitleEnabled, SettingValue = "true" });

            // Setup default auto-approval setting to false
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "false" });

            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "false" });

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                    .ReturnsAsync(() => _groups);

            _graphGroupRepository.Setup(x => x.GetUserByUpnOrIdAsync(It.IsAny<string>(), false))
                                    .ReturnsAsync(() => new AzureADUser { UserPrincipalName = "upn" } );

            _graphGroupRepository.Setup(x => x.GetUserWithOnPremisesImmutableIdAsync(It.IsAny<string>(), It.IsAny<Guid?>()))
                                    .ReturnsAsync(new AzureADUser { UserPrincipalName = "upn", OnPremisesImmutableId = "999" });

            _graphGroupRepository.Setup(x => x.GetGroupNameAsync(It.IsAny<Guid>()))
                                    .ReturnsAsync(() => "GroupNameTest");

            _teamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _serviceStatusRepository = new Mock<IServiceStatusRepository>();
            _backgroundTaskService = new Mock<IOperationsTaskQueue>();

            var destinationGuid = Guid.NewGuid();
            var ownerGuid = Guid.NewGuid();

            var destination = new DestinationObject()
            {
                Type = "GroupMembership",
                Value = new GroupDestinationValue() { ObjectId = destinationGuid }
            };

            var destinationAttributes = new DestinationAttributes()
            {
                Id = destinationGuid,
                Name = "GroupNameTest",
                Owners = new List<Guid> { ownerGuid }
            };

            _graphGroupRepository.Setup(x => x.GetDestinationOwnersAsync(It.IsAny<List<Guid>>()))
                                    .ReturnsAsync(new Dictionary<Guid, List<Guid>>() { { destinationGuid, new List<Guid> { ownerGuid } } });

            _destinationAttributesRepository.Setup(x => x.UpdateAttributes(It.IsAny<DestinationAttributes>())).Returns(Task.CompletedTask);

            var telemetryConfiguration = new TelemetryConfiguration();
            _telemetryClient = new TelemetryClient(telemetryConfiguration);

            _groupTypes = new List<string>
            {
                "Microsoft 365",
                "Security",
                "Mail enabled security",
                "Distribution"
            };

            var currentDataTimeUtc = DateTime.UtcNow;
            _jobEntities = Enumerable.Range(0, _jobCount).Select(x => new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = ((SyncStatus)Random.Shared.Next(1, 15)).ToString(),
                LastSuccessfulRunTime = currentDataTimeUtc.AddHours(-4),
                LastSuccessfulStartTime = currentDataTimeUtc.AddHours(-5),
                StartDate = currentDataTimeUtc.AddMonths(-1),
                ScheduledDate = currentDataTimeUtc.AddHours(2),
                ThresholdPercentageForAdditions = 10,
                ThresholdPercentageForRemovals = 10,
                Period = 6,
                StatusDetails = new Status
                {
                    Id = Guid.Parse("AC3604F9-5869-EE11-9937-6045BDE913DD"),
                    Name = SyncStatus.Idle.ToString(),
                    SortPriority = 1000
                },
                MembershipType = "GroupMembership",
                Group = new Group
                {
                    SyncJobId = Guid.NewGuid(),
                    GroupId = Guid.NewGuid()
                }
            }).ToList();

            _jobEntities.ForEach(x =>
            {
                _groups.Add(new AzureADGroup
                {
                    ObjectId = x.Group.GroupId,
                    Type = _groupTypes[Random.Shared.Next(0, _groupTypes.Count)]
                });
            });

            _syncJobIds = new List<string>();
            _jobEntities.ForEach(x =>
            {
                _syncJobIds.Add(x.Id.ToString());
            });

            _newSyncJob = new NewSyncJobDTO
            {
                Destination = $"[{{\"value\":{{\"objectId\":\"{destinationGuid}\"}},\"type\":\"GroupMembership\"}}]",
                Status = SyncStatus.Idle.ToString(),
                Period = 24,
                Query = "[{ \"type\": \"GroupMembership\", \"source\": \"fc8f8e1a-6d91-4965-85ff-f911944f201d\"}]",
                Requestor = "user@domain.com",
                StartDate = DateTime.UtcNow.AddDays(-1).ToString(),
                ThresholdPercentageForAdditions = 100,
                ThresholdPercentageForRemovals = 20
            };

            _databaseSyncJobsRepository.Setup(x => x.GetSyncJobsAsync())
                              .ReturnsAsync(() => _jobEntities);

            _databaseSyncJobsRepository.Setup(x => x.GetSyncJobs(It.IsAny<bool>()))
                  .Returns(() => _jobEntities.AsQueryable());

            _databaseSyncJobsRepository.Setup(repo => repo.CreateSyncJobAsync(It.IsAny<SyncJob>()))
                .ReturnsAsync(Guid.NewGuid());

            _postResetRequestHandler = new PostOperationHandler(_loggingRepository.Object,
                                                                _serviceStatusRepository.Object,
                                                                _backgroundTaskService.Object);

            _getJobsHandler = new GetJobsHandler(_loggingRepository.Object,
                                                 _databaseSyncJobsRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _httpContextAccessor.Object,
                                                 _syncJobChangeRepository.Object);

            _patchJobsHandler = new PatchJobsHandler(_loggingRepository.Object,
                                                 _databaseSyncJobsRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _thresholdConfig.Object);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _getJobDetailsHandler = new GetJobDetailsHandler(_loggingRepository.Object,
                                                _databaseSyncJobsRepository.Object,
                                                _syncJobChangeRepository.Object,
                                                _titlesRepository.Object,
                                                _graphGroupRepository.Object,
                                                _teamsChannelRepository.Object,
                                                _httpContextAccessor.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };
        }

        [TestMethod]
        [DataRow(Roles.JOB_TENANT_READER)]
        [DataRow(Roles.JOB_OWNER_WRITER)]
        [DataRow("UserRole")]
        public async Task GetJobsTestByRoleAsync(string role)
        {
            var userId = Guid.NewGuid().ToString();
            foreach (var job in _jobEntities)
            {
                job.DestinationOwners = new List<DestinationOwner>
                {
                    new DestinationOwner
                    {
                        ObjectId = Guid.Parse(userId)
                    }
                };
            }

            _context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            var response = await _jobsController.GetJobsAsync(_odataQueryOptions);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);
            var pagedResponse = result.Value as PagedResponseDTO;

            Assert.IsNotNull(pagedResponse);
            Assert.IsNotNull(pagedResponse.Items);
            Assert.IsTrue(pagedResponse.TotalNumberOfPages > 0);
            Assert.IsTrue(pagedResponse.CurrentPage > 0);
            Assert.IsTrue(pagedResponse.PageSize > 0);
            Assert.IsTrue(pagedResponse.TotalItems >= 0);
            var jobs = pagedResponse.Items.ToList();
            Assert.AreEqual(_jobCount, jobs.Count);
            Assert.AreEqual(_jobCount, jobs.Select(x => x.TargetGroupId).Distinct().Count());
            Assert.IsTrue(jobs.All(x => x.SyncJobId.ToString() != null));
            Assert.IsTrue(jobs.All(x => x.EstimatedNextRunTime == x.LastSuccessfulRunTime.AddHours(x.Period)));
            Assert.IsTrue(jobs.All(x => x.Status != null));
            Assert.IsTrue(jobs.All(x => x.TargetDestinationType != null));
        }

        [TestMethod]
        [DataRow(Roles.JOB_TENANT_READER)]
        public async Task GetJobsTestWithGraphAPIFailureAsync(string role)
        {
            _context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            _graphGroupRepository.Setup(x => x.GetGroupNameAsync(It.IsAny<Guid>()))
                                    .ReturnsAsync(() => "Example Name");

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                .ReturnsAsync(new List<AzureADGroup>());

            _getJobsHandler = new GetJobsHandler(
                                     _loggingRepository.Object,
                                     _databaseSyncJobsRepository.Object,
                                     _graphGroupRepository.Object,
                                     _httpContextAccessor.Object,
                                     _syncJobChangeRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler,_postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.GetJobsAsync(_odataQueryOptions);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);
            var pagedResponse = result.Value as PagedResponseDTO;

            Assert.IsNotNull(pagedResponse);
            Assert.IsNotNull(pagedResponse.Items);
            Assert.IsTrue(pagedResponse.TotalNumberOfPages > 0);
            Assert.IsTrue(pagedResponse.CurrentPage > 0);
            Assert.IsTrue(pagedResponse.PageSize > 0);
            Assert.IsTrue(pagedResponse.TotalItems >= 0);
            var jobs = pagedResponse.Items.ToList();
            Assert.AreEqual(_jobCount, jobs.Count);
            Assert.AreEqual(_jobCount, jobs.Select(x => x.TargetGroupId).Distinct().Count());
            Assert.IsTrue(jobs.All(x => x.SyncJobId.ToString() != null));
            Assert.IsTrue(jobs.All(x => x.EstimatedNextRunTime == x.LastSuccessfulRunTime.AddHours(x.Period)));
            Assert.IsTrue(jobs.All(x => x.Status != null));
            Assert.IsTrue(jobs.All(x => x.TargetDestinationType == MembershipTypes.GroupMembership.ToString()));

        }

        [TestMethod]
        public async Task PostJobSuccessfullyTestAsync()
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "testuser@domain.com"),
                new Claim(ClaimTypes.Upn, "testuser@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            };

            _context = CreateHttpContext(claims);

            var identity = new ClaimsIdentity(claims);
            var user = new ClaimsPrincipal(identity);

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            _getJobsHandler = new GetJobsHandler(
                                     _loggingRepository.Object,
                                     _databaseSyncJobsRepository.Object,
                                     _graphGroupRepository.Object,
                                     _httpContextAccessor.Object,
                                     _syncJobChangeRepository.Object);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            Assert.IsNotNull(result);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithTitlesAsync()
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "testuser@domain.com"),
                new Claim(ClaimTypes.Upn, "testuser@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            };

            _context = CreateHttpContext(claims);

            var identity = new ClaimsIdentity(claims);
            var user = new ClaimsPrincipal(identity);

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            _getJobsHandler = new GetJobsHandler(
                                     _loggingRepository.Object,
                                     _databaseSyncJobsRepository.Object,
                                     _graphGroupRepository.Object,
                                     _httpContextAccessor.Object,
                                     _syncJobChangeRepository.Object);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            _newSyncJob.Titles =
            [
                new NewTitle
                {
                    PartId = "f8a4308b-4483-4f52-876e-93bbcf558032",
                    Name = "Everyone in Test User 8877's org with the following summarized criteria: Building 103565"
                },
                new NewTitle
                {
                    PartId = "008e32e6-0a9e-4cbb-842e-76e1773a8e52",
                    Name = "All Users in TestGroup1Members"
                }
            ];

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            Assert.IsNotNull(result);
            _titlesRepository.Verify(x => x.SaveTitlesAsync(It.IsAny<Dictionary<string, string>>(), It.IsAny<Guid>()), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithoutTitlesAsync()
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "testuser@domain.com"),
                new Claim(ClaimTypes.Upn, "testuser@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            };

            _context = CreateHttpContext(claims);

            var identity = new ClaimsIdentity(claims);
            var user = new ClaimsPrincipal(identity);

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAITitleEnabled))
                                     .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAITitleEnabled, SettingValue = "false" });

            _getJobsHandler = new GetJobsHandler(
                                     _loggingRepository.Object,
                                     _databaseSyncJobsRepository.Object,
                                     _graphGroupRepository.Object,
                                     _httpContextAccessor.Object,
                                     _syncJobChangeRepository.Object);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            _newSyncJob.Titles =
            [
                new NewTitle
                {
                    PartId = "f8a4308b-4483-4f52-876e-93bbcf558032",
                    Name = "Everyone in Test User 8877's org with the following summarized criteria: Building 103565"
                },
                new NewTitle
                {
                    PartId = "008e32e6-0a9e-4cbb-842e-76e1773a8e52",
                    Name = "All Users in TestGroup1Members"
                }
            ];

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            Assert.IsNotNull(result);
            _titlesRepository.Verify(x => x.SaveTitlesAsync(It.IsAny<Dictionary<string, string>>(), It.IsAny<Guid>()), Times.Never);
        }

        [TestMethod]
        public async Task PostJobFailureDueToRepositoryExceptionTestAsync()
        {
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            _databaseSyncJobsRepository
                .Setup(x => x.CreateSyncJobAsync(It.IsAny<SyncJob>()))
                .ThrowsAsync(new Exception("Database error"));

            var response = await _jobsController.PostJobAsync(_newSyncJob);

            Assert.IsInstanceOfType(response, typeof(ObjectResult));
            var result = response as ObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, result.StatusCode);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        public async Task PostJobCreationFailedTestAsync()
        {
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            _databaseSyncJobsRepository.Setup(x => x.CreateSyncJobAsync(It.IsAny<SyncJob>()))
                                       .ReturnsAsync(Guid.Empty);

            var response = await _jobsController.PostJobAsync(_newSyncJob);

            Assert.IsInstanceOfType(response, typeof(ObjectResult));
            var result = response as ObjectResult;
            Assert.IsNotNull(result);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        public async Task PostJobCreationWhenClaimIsNotFoundTestAsync()
        {
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER)
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            _databaseSyncJobsRepository.Setup(x => x.CreateSyncJobAsync(It.IsAny<SyncJob>()))
                                       .ReturnsAsync(Guid.Empty);

            var response = await _jobsController.PostJobAsync(_newSyncJob);

            Assert.IsInstanceOfType(response, typeof(ForbidResult));
            var result = response as ForbidResult;
            Assert.IsInstanceOfType(result, typeof(ForbidResult));

            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.IsAny<SyncJob>()), Times.Never);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        public async Task PostJobCreationNotOwnerFailureForbiddenTestAsync()
        {
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_OWNER_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                                    .ReturnsAsync(() => false);

            _databaseSyncJobsRepository.Setup(x => x.CreateSyncJobAsync(It.IsAny<SyncJob>()))
                                       .ReturnsAsync(Guid.Empty);

            var response = await _jobsController.PostJobAsync(_newSyncJob);

            Assert.IsInstanceOfType(response, typeof(ForbidResult));
            var result = response as ForbidResult;
            Assert.IsInstanceOfType(result, typeof(ForbidResult));

            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.IsAny<SyncJob>()), Times.Never);
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        public async Task PostJobCreationExceptionTestAsync()
        {
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            _databaseSyncJobsRepository.Setup(x => x.CreateSyncJobAsync(It.IsAny<SyncJob>()))
                                       .ThrowsAsync(new Exception("Database error"));

            var response = await _jobsController.PostJobAsync(_newSyncJob);

            Assert.IsNotNull(response);
            Assert.IsInstanceOfType(response, typeof(ObjectResult));
            var result = response as ObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, result.StatusCode);
        }

        [TestMethod]
        public async Task BulkApproveTestAsync()
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "testuser@domain.com"),
                new Claim(ClaimTypes.Upn, "testuser@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            };

            _context = CreateHttpContext(claims);

            var identity = new ClaimsIdentity(claims);
            var user = new ClaimsPrincipal(identity);

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            _patchJobsHandler = new PatchJobsHandler(_loggingRepository.Object,
                                                 _databaseSyncJobsRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _thresholdConfig.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.BulkApproveJobsAsync(_syncJobIds.ToArray());
            Assert.IsNotNull(response);
            var okResult = response.Result as OkObjectResult;
            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);
            var res = okResult.Value as PatchJobsResponse;
            Assert.IsNotNull(res);
            _databaseSyncJobsRepository.Verify(x => x.BulkApproveSyncJobsAsync(It.IsAny<List<string>>(), 2), Times.Once);
            _syncJobChangeRepository.Verify(x => x.BulkSaveAsync(It.IsAny<IEnumerable<SyncJobChange>>()), Times.Once);
        }

        [TestMethod]
        public async Task BulkApproveExceptionTestAsync()
        {
            _databaseSyncJobsRepository.Setup(x => x.BulkApproveSyncJobsAsync(It.IsAny<List<string>>(), It.IsAny<int?>()))
                                        .ThrowsAsync(new Exception());

            var response = await _jobsController.BulkApproveJobsAsync(_syncJobIds.ToArray());

            Assert.IsInstanceOfType(response, typeof(ActionResult<int>));

            var statusCodeResult = response.Result as StatusCodeResult;

            Assert.IsNotNull(statusCodeResult);
            Assert.AreEqual((int)HttpStatusCode.InternalServerError, statusCodeResult.StatusCode);
        }

        [TestMethod]
        public async Task BulkApprove_SetsThresholdViolationsToNotifyMinusOne_WhenApprovingJobs()
        {
            // Arrange
            var syncJobId = Guid.NewGuid();
            var syncJobIds = new List<string> { syncJobId.ToString() };
            
            _syncJobIds = syncJobIds;

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, "testuser@domain.com"),
                new Claim(ClaimTypes.Upn, "testuser@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            };

            _context = CreateHttpContext(claims);

            var identity = new ClaimsIdentity(claims);
            var user = new ClaimsPrincipal(identity);

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup threshold config to return 5 for NumberOfThresholdViolationsToNotify
            _thresholdConfig.Setup(x => x.NumberOfThresholdViolationsToNotify).Returns(5);

            _patchJobsHandler = new PatchJobsHandler(_loggingRepository.Object,
                                                 _databaseSyncJobsRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _thresholdConfig.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            // Act
            var response = await _jobsController.BulkApproveJobsAsync(_syncJobIds.ToArray());

            // Assert
            Assert.IsNotNull(response);
            var okResult = response.Result as OkObjectResult;
            Assert.IsNotNull(okResult);
            
            // Verify that BulkApproveSyncJobsAsync was called with threshold violations = 4 (5 - 1)
            _databaseSyncJobsRepository.Verify(x => x.BulkApproveSyncJobsAsync(
                It.Is<List<string>>(ids => ids.Count == 1 && ids[0] == syncJobId.ToString()), 
                4), 
                Times.Once);
            _syncJobChangeRepository.Verify(x => x.BulkSaveAsync(It.IsAny<IEnumerable<SyncJobChange>>()), Times.Once);
        }


        [TestMethod]
        public async Task GetJobs_LastModifiedTime_UsesAnyChangeReason()
        {
            // Arrange - override the large initialized dataset with a single controlled job
            var job = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = SyncStatus.Idle.ToString(),
                Period = 6,
                LastRunTime = DateTime.UtcNow.AddHours(-5),
                LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-6),
                StartDate = DateTime.UtcNow.AddDays(-2),
                ScheduledDate = DateTime.UtcNow.AddHours(-1),
                MembershipType = MembershipTypes.GroupMembership.ToString(),
                StatusDetails = new Status { Id = Guid.NewGuid(), Name = SyncStatus.Idle.ToString(), SortPriority = 1000 },
                Group = new Group { SyncJobId = Guid.NewGuid(), GroupId = Guid.NewGuid() }
            };

            var singleList = new List<SyncJob> { job };
            _databaseSyncJobsRepository.Setup(x => x.GetSyncJobs(It.IsAny<bool>()))
                                       .Returns(singleList.AsQueryable());

            var latestChangeTime = DateTime.UtcNow.AddMinutes(-17).AddSeconds(-DateTime.UtcNow.Second); // normalize seconds
            _syncJobChangeRepository.Setup(x => x.GetLastSyncJobRecordBySyncJobIdAsync(job.Id))
                                     .ReturnsAsync(new SyncJobChange
                                     {
                                         Id = Guid.NewGuid(),
                                         SyncJobId = job.Id,
                                         ChangeTime = latestChangeTime,
                                         ChangeReason = SyncJobChangeReason.SubmissionApproved.ToString()
                                     });

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                 .ReturnsAsync(new List<AzureADGroup>{ new AzureADGroup { ObjectId = job.Group.GroupId, Name = "LastModTestGroup" } });

            var userContext = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(userContext);

            var response = await _jobsController.GetJobsAsync(_odataQueryOptions);
            var ok = response.Result as OkObjectResult;
            Assert.IsNotNull(ok, "Expected OkObjectResult");
            var paged = ok!.Value as PagedResponseDTO;
            Assert.IsNotNull(paged, "Expected paged response");
            var first = paged!.Items.Single();

            Assert.AreEqual(latestChangeTime, first.LastModifiedTime, "LastModifiedTime should reflect most recent ANY change (SubmissionApproved)");
            _syncJobChangeRepository.Verify(x => x.GetLastSyncJobRecordBySyncJobIdAsync(job.Id), Times.AtLeastOnce);
        }

        [TestMethod]
        public async Task GetJobs_CustomSort_TargetGroupName_Ascending()
        {
            var jobs = new List<SyncJob>
            {
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    Status = SyncStatus.Idle.ToString(),
                    Period = 6,
                    LastRunTime = DateTime.UtcNow.AddHours(-5),
                    LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-6),
                    StartDate = DateTime.UtcNow.AddDays(-2),
                    ScheduledDate = DateTime.UtcNow.AddHours(-1),
                    MembershipType = MembershipTypes.GroupMembership.ToString(),
                    StatusDetails = new Status { Id = Guid.NewGuid(), Name = SyncStatus.Idle.ToString(), SortPriority = 1000 },
                    Group = new Group { SyncJobId = Guid.NewGuid(), GroupId = Guid.NewGuid() }
                },
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    Status = SyncStatus.Idle.ToString(),
                    Period = 6,
                    LastRunTime = DateTime.UtcNow.AddHours(-5),
                    LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-6),
                    StartDate = DateTime.UtcNow.AddDays(-2),
                    ScheduledDate = DateTime.UtcNow.AddHours(-1),
                    MembershipType = MembershipTypes.GroupMembership.ToString(),
                    StatusDetails = new Status { Id = Guid.NewGuid(), Name = SyncStatus.Idle.ToString(), SortPriority = 1000 },
                    Group = new Group { SyncJobId = Guid.NewGuid(), GroupId = Guid.NewGuid() }
                }
            };

            _databaseSyncJobsRepository.Setup(x => x.GetSyncJobs(It.IsAny<bool>()))
                                       .Returns(jobs.AsQueryable());

            var groups = new List<AzureADGroup>
            {
                new AzureADGroup { ObjectId = jobs[0].Group.GroupId, Name = "ZZZ Last Group" },
                new AzureADGroup { ObjectId = jobs[1].Group.GroupId, Name = "AAA First Group" }
            };

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                 .ReturnsAsync(groups);

            _syncJobChangeRepository.Setup(x => x.GetLastSyncJobRecordBySyncJobIdAsync(It.IsAny<Guid>()))
                                     .ReturnsAsync((SyncJobChange?)null);

            var userContext = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(userContext);

            var request = new GetJobsRequest
            {
                CustomSortBy = "targetGroupName",
                IsSortedDescending = false
            };

            var handler = new GetJobsHandler(_loggingRepository.Object,
                                             _databaseSyncJobsRepository.Object,
                                             _graphGroupRepository.Object,
                                             _httpContextAccessor.Object,
                                             _syncJobChangeRepository.Object);

            var response = await handler.ExecuteAsync(request);

            Assert.AreEqual(2, response.Model.Count);
            Assert.AreEqual("AAA First Group", response.Model[0].TargetGroupName, "First job should be sorted alphabetically first");
            Assert.AreEqual("ZZZ Last Group", response.Model[1].TargetGroupName, "Second job should be sorted alphabetically last");
        }

        [TestMethod]
        public async Task GetJobs_CustomSort_TargetGroupName_Descending()
        {
            var jobs = new List<SyncJob>
            {
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    Status = SyncStatus.Idle.ToString(),
                    Period = 6,
                    LastRunTime = DateTime.UtcNow.AddHours(-5),
                    LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-6),
                    StartDate = DateTime.UtcNow.AddDays(-2),
                    ScheduledDate = DateTime.UtcNow.AddHours(-1),
                    MembershipType = MembershipTypes.GroupMembership.ToString(),
                    StatusDetails = new Status { Id = Guid.NewGuid(), Name = SyncStatus.Idle.ToString(), SortPriority = 1000 },
                    Group = new Group { SyncJobId = Guid.NewGuid(), GroupId = Guid.NewGuid() }
                },
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    Status = SyncStatus.Idle.ToString(),
                    Period = 6,
                    LastRunTime = DateTime.UtcNow.AddHours(-5),
                    LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-6),
                    StartDate = DateTime.UtcNow.AddDays(-2),
                    ScheduledDate = DateTime.UtcNow.AddHours(-1),
                    MembershipType = MembershipTypes.GroupMembership.ToString(),
                    StatusDetails = new Status { Id = Guid.NewGuid(), Name = SyncStatus.Idle.ToString(), SortPriority = 1000 },
                    Group = new Group { SyncJobId = Guid.NewGuid(), GroupId = Guid.NewGuid() }
                }
            };

            _databaseSyncJobsRepository.Setup(x => x.GetSyncJobs(It.IsAny<bool>()))
                                       .Returns(jobs.AsQueryable());

            var groups = new List<AzureADGroup>
            {
                new AzureADGroup { ObjectId = jobs[0].Group.GroupId, Name = "AAA First Group" },
                new AzureADGroup { ObjectId = jobs[1].Group.GroupId, Name = "ZZZ Last Group" }
            };

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                 .ReturnsAsync(groups);

            _syncJobChangeRepository.Setup(x => x.GetLastSyncJobRecordBySyncJobIdAsync(It.IsAny<Guid>()))
                                     .ReturnsAsync((SyncJobChange?)null);

            var userContext = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(userContext);

            var request = new GetJobsRequest
            {
                CustomSortBy = "targetGroupName",
                IsSortedDescending = true
            };

            var handler = new GetJobsHandler(_loggingRepository.Object,
                                             _databaseSyncJobsRepository.Object,
                                             _graphGroupRepository.Object,
                                             _httpContextAccessor.Object,
                                             _syncJobChangeRepository.Object);

            var response = await handler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(2, response.Model.Count);
            Assert.AreEqual("ZZZ Last Group", response.Model[0].TargetGroupName, "First job should be sorted reverse alphabetically first");
            Assert.AreEqual("AAA First Group", response.Model[1].TargetGroupName, "Second job should be sorted reverse alphabetically last");
        }

        [TestMethod]
        public async Task GetJobs_CustomSort_LastModifiedTime_Ascending()
        {
            // Arrange - create multiple jobs with different last modified times
            var jobs = new List<SyncJob>
            {
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    Status = SyncStatus.Idle.ToString(),
                    Period = 6,
                    LastRunTime = DateTime.UtcNow.AddHours(-5),
                    LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-6),
                    StartDate = DateTime.UtcNow.AddDays(-2),
                    ScheduledDate = DateTime.UtcNow.AddHours(-1),
                    MembershipType = MembershipTypes.GroupMembership.ToString(),
                    StatusDetails = new Status { Id = Guid.NewGuid(), Name = SyncStatus.Idle.ToString(), SortPriority = 1000 },
                    Group = new Group { SyncJobId = Guid.NewGuid(), GroupId = Guid.NewGuid() }
                },
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    Status = SyncStatus.Idle.ToString(),
                    Period = 6,
                    LastRunTime = DateTime.UtcNow.AddHours(-5),
                    LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-6),
                    StartDate = DateTime.UtcNow.AddDays(-2),
                    ScheduledDate = DateTime.UtcNow.AddHours(-1),
                    MembershipType = MembershipTypes.GroupMembership.ToString(),
                    StatusDetails = new Status { Id = Guid.NewGuid(), Name = SyncStatus.Idle.ToString(), SortPriority = 1000 },
                    Group = new Group { SyncJobId = Guid.NewGuid(), GroupId = Guid.NewGuid() }
                }
            };

            _databaseSyncJobsRepository.Setup(x => x.GetSyncJobs(It.IsAny<bool>()))
                                       .Returns(jobs.AsQueryable());

            var groups = new List<AzureADGroup>
            {
                new AzureADGroup { ObjectId = jobs[0].Group.GroupId, Name = "Group A" },
                new AzureADGroup { ObjectId = jobs[1].Group.GroupId, Name = "Group B" }
            };

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                 .ReturnsAsync(groups);

            var earlierTime = DateTime.UtcNow.AddHours(-2);
            var laterTime = DateTime.UtcNow.AddHours(-1);

            _syncJobChangeRepository.Setup(x => x.GetLastSyncJobRecordBySyncJobIdAsync(jobs[0].Id))
                                     .ReturnsAsync(new SyncJobChange { Id = Guid.NewGuid(), SyncJobId = jobs[0].Id, ChangeTime = laterTime });

            _syncJobChangeRepository.Setup(x => x.GetLastSyncJobRecordBySyncJobIdAsync(jobs[1].Id))
                                     .ReturnsAsync(new SyncJobChange { Id = Guid.NewGuid(), SyncJobId = jobs[1].Id, ChangeTime = earlierTime });

            var userContext = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(userContext);

            // Create request with custom sort by lastModifiedTime ascending
            var request = new GetJobsRequest
            {
                CustomSortBy = "lastModifiedTime",
                IsSortedDescending = false
            };

            var handler = new GetJobsHandler(_loggingRepository.Object,
                                             _databaseSyncJobsRepository.Object,
                                             _graphGroupRepository.Object,
                                             _httpContextAccessor.Object,
                                             _syncJobChangeRepository.Object);

            // Act
            var response = await handler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(2, response.Model.Count);
            Assert.AreEqual(earlierTime, response.Model[0].LastModifiedTime, "Earlier modified job should be first");
            Assert.AreEqual(laterTime, response.Model[1].LastModifiedTime, "Later modified job should be second");
        }

        [TestMethod]
        public async Task GetJobs_CustomSort_LastModifiedTime_Descending()
        {
            // Arrange - create multiple jobs with different last modified times
            var jobs = new List<SyncJob>
            {
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    Status = SyncStatus.Idle.ToString(),
                    Period = 6,
                    LastRunTime = DateTime.UtcNow.AddHours(-5),
                    LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-6),
                    StartDate = DateTime.UtcNow.AddDays(-2),
                    ScheduledDate = DateTime.UtcNow.AddHours(-1),
                    MembershipType = MembershipTypes.GroupMembership.ToString(),
                    StatusDetails = new Status { Id = Guid.NewGuid(), Name = SyncStatus.Idle.ToString(), SortPriority = 1000 },
                    Group = new Group { SyncJobId = Guid.NewGuid(), GroupId = Guid.NewGuid() }
                },
                new SyncJob
                {
                    Id = Guid.NewGuid(),
                    Status = SyncStatus.Idle.ToString(),
                    Period = 6,
                    LastRunTime = DateTime.UtcNow.AddHours(-5),
                    LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-6),
                    StartDate = DateTime.UtcNow.AddDays(-2),
                    ScheduledDate = DateTime.UtcNow.AddHours(-1),
                    MembershipType = MembershipTypes.GroupMembership.ToString(),
                    StatusDetails = new Status { Id = Guid.NewGuid(), Name = SyncStatus.Idle.ToString(), SortPriority = 1000 },
                    Group = new Group { SyncJobId = Guid.NewGuid(), GroupId = Guid.NewGuid() }
                }
            };

            _databaseSyncJobsRepository.Setup(x => x.GetSyncJobs(It.IsAny<bool>()))
                                       .Returns(jobs.AsQueryable());

            var groups = new List<AzureADGroup>
            {
                new AzureADGroup { ObjectId = jobs[0].Group.GroupId, Name = "Group A" },
                new AzureADGroup { ObjectId = jobs[1].Group.GroupId, Name = "Group B" }
            };

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                 .ReturnsAsync(groups);

            var earlierTime = DateTime.UtcNow.AddHours(-2);
            var laterTime = DateTime.UtcNow.AddHours(-1);

            _syncJobChangeRepository.Setup(x => x.GetLastSyncJobRecordBySyncJobIdAsync(jobs[0].Id))
                                     .ReturnsAsync(new SyncJobChange { Id = Guid.NewGuid(), SyncJobId = jobs[0].Id, ChangeTime = earlierTime });

            _syncJobChangeRepository.Setup(x => x.GetLastSyncJobRecordBySyncJobIdAsync(jobs[1].Id))
                                     .ReturnsAsync(new SyncJobChange { Id = Guid.NewGuid(), SyncJobId = jobs[1].Id, ChangeTime = laterTime });

            var userContext = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(userContext);

            // Create request with custom sort by lastModifiedTime descending
            var request = new GetJobsRequest
            {
                CustomSortBy = "lastModifiedTime",
                IsSortedDescending = true
            };

            var handler = new GetJobsHandler(_loggingRepository.Object,
                                             _databaseSyncJobsRepository.Object,
                                             _graphGroupRepository.Object,
                                             _httpContextAccessor.Object,
                                             _syncJobChangeRepository.Object);

            // Act
            var response = await handler.ExecuteAsync(request);

            // Assert
            Assert.AreEqual(2, response.Model.Count);
            Assert.AreEqual(laterTime, response.Model[0].LastModifiedTime, "Later modified job should be first when descending");
            Assert.AreEqual(earlierTime, response.Model[1].LastModifiedTime, "Earlier modified job should be second when descending");
        }

        [TestMethod]
        public async Task GetJobs_SyncJobChangeRepository_Exception_Handled()
        {
            // Arrange - create job that will throw exception when fetching last modified time
            var job = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = SyncStatus.Idle.ToString(),
                Period = 6,
                LastRunTime = DateTime.UtcNow.AddHours(-5),
                LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-6),
                StartDate = DateTime.UtcNow.AddDays(-2),
                ScheduledDate = DateTime.UtcNow.AddHours(-1),
                MembershipType = MembershipTypes.GroupMembership.ToString(),
                StatusDetails = new Status { Id = Guid.NewGuid(), Name = SyncStatus.Idle.ToString(), SortPriority = 1000 },
                Group = new Group { SyncJobId = Guid.NewGuid(), GroupId = Guid.NewGuid() }
            };

            _databaseSyncJobsRepository.Setup(x => x.GetSyncJobs(It.IsAny<bool>()))
                                       .Returns(new List<SyncJob> { job }.AsQueryable());

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                 .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = job.Group.GroupId, Name = "TestGroup" } });

            // Setup to throw exception
            _syncJobChangeRepository.Setup(x => x.GetLastSyncJobRecordBySyncJobIdAsync(job.Id))
                                     .ThrowsAsync(new Exception("Database error"));

            var userContext = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(userContext);

            var handler = new GetJobsHandler(_loggingRepository.Object,
                                             _databaseSyncJobsRepository.Object,
                                             _graphGroupRepository.Object,
                                             _httpContextAccessor.Object,
                                             _syncJobChangeRepository.Object);

            // Act
            var response = await handler.ExecuteAsync(new GetJobsRequest());

            // Assert - should handle exception gracefully and return null LastModifiedTime
            Assert.AreEqual(1, response.Model.Count);
            Assert.IsNull(response.Model[0].LastModifiedTime, "LastModifiedTime should be null when exception occurs");
        }

        [TestMethod]
        public async Task GetJobs_TeamsChannelMembership_HandlesChannelGroupId()
        {
            // Arrange - create job with TeamsChannelMembership type
            var job = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = SyncStatus.Idle.ToString(),
                Period = 6,
                LastRunTime = DateTime.UtcNow.AddHours(-5),
                LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-6),
                StartDate = DateTime.UtcNow.AddDays(-2),
                ScheduledDate = DateTime.UtcNow.AddHours(-1),
                MembershipType = MembershipTypes.TeamsChannelMembership.ToString(),
                StatusDetails = new Status { Id = Guid.NewGuid(), Name = SyncStatus.Idle.ToString(), SortPriority = 1000 },
                Group = new Group { SyncJobId = Guid.NewGuid(), GroupId = Guid.NewGuid() },
                Channel = new Channel { SyncJobId = Guid.NewGuid(), GroupId = Guid.NewGuid(), ChannelId = "channel123" }
            };

            _databaseSyncJobsRepository.Setup(x => x.GetSyncJobs(It.IsAny<bool>()))
                                       .Returns(new List<SyncJob> { job }.AsQueryable());

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                 .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = job.Channel.GroupId, Name = "TeamsGroup" } });

            _syncJobChangeRepository.Setup(x => x.GetLastSyncJobRecordBySyncJobIdAsync(job.Id))
                                     .ReturnsAsync((SyncJobChange?)null);

            var userContext = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(userContext);

            var handler = new GetJobsHandler(_loggingRepository.Object,
                                             _databaseSyncJobsRepository.Object,
                                             _graphGroupRepository.Object,
                                             _httpContextAccessor.Object,
                                             _syncJobChangeRepository.Object);

            // Act
            var response = await handler.ExecuteAsync(new GetJobsRequest());

            // Assert
            Assert.AreEqual(1, response.Model.Count);
            Assert.AreEqual(job.Channel.GroupId, response.Model[0].TargetGroupId, "Should use Channel.GroupId for Teams membership");
            Assert.AreEqual("TeamsGroup", response.Model[0].TargetGroupName);
        }

        [TestMethod]
        public async Task GetJobs_NoQueryOptions_UsesDefaultSortAndPagination()
        {
            // Arrange
            var job = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = SyncStatus.Idle.ToString(),
                Period = 6,
                LastRunTime = DateTime.UtcNow.AddHours(-5),
                LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-6),
                StartDate = DateTime.UtcNow.AddDays(-2),
                ScheduledDate = DateTime.UtcNow.AddHours(-1),
                MembershipType = MembershipTypes.GroupMembership.ToString(),
                StatusDetails = new Status { Id = Guid.NewGuid(), Name = SyncStatus.Idle.ToString(), SortPriority = 1000 },
                Group = new Group { SyncJobId = Guid.NewGuid(), GroupId = Guid.NewGuid() }
            };

            _databaseSyncJobsRepository.Setup(x => x.GetSyncJobs(It.IsAny<bool>()))
                                       .Returns(new List<SyncJob> { job }.AsQueryable());

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                 .ReturnsAsync(new List<AzureADGroup> { new AzureADGroup { ObjectId = job.Group.GroupId, Name = "TestGroup" } });

            _syncJobChangeRepository.Setup(x => x.GetLastSyncJobRecordBySyncJobIdAsync(job.Id))
                                     .ReturnsAsync((SyncJobChange?)null);

            var userContext = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(userContext);

            var handler = new GetJobsHandler(_loggingRepository.Object,
                                             _databaseSyncJobsRepository.Object,
                                             _graphGroupRepository.Object,
                                             _httpContextAccessor.Object,
                                             _syncJobChangeRepository.Object);

            // Act - no query options provided
            var response = await handler.ExecuteAsync(new GetJobsRequest { QueryOptions = null });

            // Assert
            Assert.AreEqual(1, response.TotalItems);
            Assert.AreEqual(1, response.TotalNumberOfPages);
            Assert.AreEqual(1, response.CurrentPage);
            Assert.AreEqual(1, response.Model.Count);
        }

        [TestMethod]
        public async Task GetJobs_UserWithoutPermissions_ReturnsEmptyResults()
        {
            // Arrange - user without proper claims
            var userContext = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, "SomeOtherRole")
                // Missing objectidentifier claim
            });
            _httpContextAccessor.Setup(x => x.HttpContext).Returns(userContext);

            var handler = new GetJobsHandler(_loggingRepository.Object,
                                             _databaseSyncJobsRepository.Object,
                                             _graphGroupRepository.Object,
                                             _httpContextAccessor.Object,
                                             _syncJobChangeRepository.Object);

            var response = await handler.ExecuteAsync(new GetJobsRequest());

            Assert.AreEqual(0, response.Model.Count, "Should return empty results for user without permissions");
            Assert.AreEqual(0, response.TotalItems);
        }

        private async IAsyncEnumerable<T> ToAsyncEnumerable<T>(IEnumerable<T> input)
        {
            foreach (var value in await Task.FromResult(input))
            {
                yield return value;
            }
        }

        private HttpContext CreateHttpContext(List<Claim> claims)
        {
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var principal = new ClaimsPrincipal(identity);
            var httpContext = new DefaultHttpContext();
            httpContext.User = principal;

            return httpContext;
        }

        [TestMethod]
        public async Task PostJobWithAutoApprovalEnabledAndValidGroupsTestAsync()
        {
            // Setup context
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup auto-approval setting to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });

            // Setup groups with acceptable visibility (not HiddenMembership)
            var groupId1 = Guid.NewGuid();
            var groupId2 = Guid.NewGuid();
            _groups.AddRange(new List<AzureADGroup>
            {
                new AzureADGroup { ObjectId = groupId1, Visibility = "Public" },
                new AzureADGroup { ObjectId = groupId2, Visibility = "Private" }
            });

            // Setup sync job with GroupMembership query
            _newSyncJob.Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId1}\"}},{{\"type\":\"GroupMembership\",\"source\":\"{groupId2}\"}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was auto-approved (status should be Idle)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.Idle.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with OnboardingAutoApproved reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.OnboardingAutoApproved.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithAutoApprovalEnabledButHiddenMembershipGroupTestAsync()
        {
            // Setup context
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup auto-approval setting to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });

            // Setup groups with one having HiddenMembership visibility
            var groupId1 = Guid.NewGuid();
            var groupId2 = Guid.NewGuid();
            _groups.AddRange(new List<AzureADGroup>
            {
                new AzureADGroup { ObjectId = groupId1, Visibility = "Public" },
                new AzureADGroup { ObjectId = groupId2, Visibility = "HiddenMembership" }
            });

            // Setup sync job with GroupMembership query
            _newSyncJob.Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId1}\"}},{{\"type\":\"GroupMembership\",\"source\":\"{groupId2}\"}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was NOT auto-approved (status should be PendingReview)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithAutoApprovalDisabledTestAsync()
        {
            // Setup context
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup auto-approval setting to disabled (default)
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "false" });

            // Setup groups with acceptable visibility
            var groupId1 = Guid.NewGuid();
            var groupId2 = Guid.NewGuid();
            _groups.AddRange(new List<AzureADGroup>
            {
                new AzureADGroup { ObjectId = groupId1, Visibility = "Public" },
                new AzureADGroup { ObjectId = groupId2, Visibility = "Private" }
            });

            // Setup sync job with GroupMembership query
            _newSyncJob.Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId1}\"}},{{\"type\":\"GroupMembership\",\"source\":\"{groupId2}\"}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was NOT auto-approved (status should be PendingReview)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithAutoApprovalEnabledButNonGroupMembershipQueryTestAsync()
        {
            // Setup context
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup auto-approval setting to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });

            // Setup sync job with non-GroupMembership query (e.g., SecurityGroup)
            var groupId = Guid.NewGuid();
            _newSyncJob.Query = $"[{{\"type\":\"SecurityGroup\",\"source\":\"{groupId}\"}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was NOT auto-approved (status should be PendingReview)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        // New tests for org leader auto-approval scenarios

        [TestMethod]
        public async Task PostJobWithOrgLeaderAutoApprovalEnabledAndMatchingManagerIdTestAsync()
        {
            // Setup context
            var userUpn = "user@domain.com";
            var userId = Guid.NewGuid().ToString();
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, userUpn),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup group-based auto-approval setting to disabled (ensure it doesn't interfere)
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "false" });

            // Setup org leader auto-approval setting to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "true" });

            // Setup user with onPremisesImmutableId matching manager ID
            var userOnPremisesImmutableId = "12345";
            _graphGroupRepository.Setup(x => x.GetUserWithOnPremisesImmutableIdAsync(userId, It.IsAny<Guid?>()))
                                .ReturnsAsync(new AzureADUser { 
                                    UserPrincipalName = userUpn, 
                                    OnPremisesImmutableId = userOnPremisesImmutableId 
                                });

            // Setup sync job with single SqlMembership query where manager ID matches user's onPremisesImmutableId
            _newSyncJob.Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{userOnPremisesImmutableId}}}}}}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was auto-approved (status should be Idle)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.Idle.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with OnboardingAutoApproved reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.OnboardingAutoApproved.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithOrgLeaderAutoApprovalEnabledButNonMatchingManagerIdTestAsync()
        {
            // Setup context
            var userUpn = "user@domain.com";
            var userId = Guid.NewGuid().ToString();
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, userUpn),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup org leader auto-approval setting to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "true" });

            // Setup user with onPremisesImmutableId different from manager ID
            var userOnPremisesImmutableId = "12345";
            var differentManagerId = "67890";
            _graphGroupRepository.Setup(x => x.GetUserWithOnPremisesImmutableIdAsync(userId, It.IsAny<Guid?>()))
                                .ReturnsAsync(new AzureADUser { 
                                    UserPrincipalName = userUpn, 
                                    OnPremisesImmutableId = userOnPremisesImmutableId 
                                });

            // Setup sync job with single SqlMembership query where manager ID does NOT match user's onPremisesImmutableId
            _newSyncJob.Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{differentManagerId}}}}}}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was NOT auto-approved (status should be PendingReview)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithOrgLeaderAutoApprovalDisabledTestAsync()
        {
            // Setup context
            var userUpn = "user@domain.com";
            var userId = Guid.NewGuid().ToString();
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, userUpn),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup org leader auto-approval setting to disabled (default)
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "false" });

            // Setup user with onPremisesImmutableId matching manager ID
            var userOnPremisesImmutableId = "12345";
            _graphGroupRepository.Setup(x => x.GetUserWithOnPremisesImmutableIdAsync(userId, It.IsAny<Guid?>()))
                                .ReturnsAsync(new AzureADUser { 
                                    UserPrincipalName = userUpn, 
                                    OnPremisesImmutableId = userOnPremisesImmutableId 
                                });

            // Setup sync job with single SqlMembership query where manager ID matches user's onPremisesImmutableId
            _newSyncJob.Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{userOnPremisesImmutableId}}}}}}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was NOT auto-approved (status should be PendingReview)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithOrgLeaderAutoApprovalEnabledButMultipleSqlMembershipQueriesTestAsync()
        {
            // Setup context
            var userUpn = "user@domain.com";
            var userId = Guid.NewGuid().ToString();
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, userUpn),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup org leader auto-approval setting to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "true" });

            // Setup user with onPremisesImmutableId
            var userOnPremisesImmutableId = "12345";
            _graphGroupRepository.Setup(x => x.GetUserWithOnPremisesImmutableIdAsync(userId, It.IsAny<Guid?>()))
                                .ReturnsAsync(new AzureADUser { 
                                    UserPrincipalName = userUpn, 
                                    OnPremisesImmutableId = userOnPremisesImmutableId 
                                });

            // Setup sync job with multiple SqlMembership queries (should not auto-approve)
            _newSyncJob.Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{userOnPremisesImmutableId}}}}}}},{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{userOnPremisesImmutableId}}}}}}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was NOT auto-approved (status should be PendingReview)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithOrgLeaderAutoApprovalEnabledButNonSqlMembershipQueryTestAsync()
        {
            // Setup context
            var userUpn = "user@domain.com";
            var userId = Guid.NewGuid().ToString();
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, userUpn),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup org leader auto-approval setting to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "true" });

            // Setup user with onPremisesImmutableId
            var userOnPremisesImmutableId = "12345";
            _graphGroupRepository.Setup(x => x.GetUserWithOnPremisesImmutableIdAsync(userId, It.IsAny<Guid?>()))
                                .ReturnsAsync(new AzureADUser { 
                                    UserPrincipalName = userUpn, 
                                    OnPremisesImmutableId = userOnPremisesImmutableId 
                                });

            // Setup sync job with non-SqlMembership query (e.g., GroupMembership)
            var groupId = Guid.NewGuid();
            _newSyncJob.Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was NOT auto-approved (status should be PendingReview)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithOrgLeaderAutoApprovalEnabledButUserWithoutOnPremisesImmutableIdTestAsync()
        {
            // Setup context
            var userUpn = "user@domain.com";
            var userId = Guid.NewGuid().ToString();
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, userUpn),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup org leader auto-approval setting to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "true" });

            // Setup user without onPremisesImmutableId (null or empty)
            _graphGroupRepository.Setup(x => x.GetUserWithOnPremisesImmutableIdAsync(userId, It.IsAny<Guid?>()))
                                .ReturnsAsync(new AzureADUser { 
                                    UserPrincipalName = userUpn, 
                                    OnPremisesImmutableId = null // User has no onPremisesImmutableId
                                });

            // Setup sync job with single SqlMembership query
            var managerId = "12345";
            _newSyncJob.Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}}}}}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was NOT auto-approved (status should be PendingReview)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithBothAutoApprovalSettingsEnabledButOnlyGroupBasedMatches_TestAsync()
        {
            // Setup context
            var userUpn = "user@domain.com";
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, userUpn),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup both auto-approval settings to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "true" });

            // Setup groups with acceptable visibility
            var groupId1 = Guid.NewGuid();
            var groupId2 = Guid.NewGuid();
            _groups.AddRange(new List<AzureADGroup>
            {
                new AzureADGroup { ObjectId = groupId1, Visibility = "Public" },
                new AzureADGroup { ObjectId = groupId2, Visibility = "Private" }
            });

            // Setup user with onPremisesImmutableId
            var userOnPremisesImmutableId = "12345";
            _graphGroupRepository.Setup(x => x.GetUserByUpnOrIdAsync(userUpn, false))
                                .ReturnsAsync(new AzureADUser { 
                                    UserPrincipalName = userUpn, 
                                    OnPremisesImmutableId = userOnPremisesImmutableId 
                                });

            // Setup sync job with GroupMembership query (should trigger group-based auto-approval)
            _newSyncJob.Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId1}\"}},{{\"type\":\"GroupMembership\",\"source\":\"{groupId2}\"}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was auto-approved (status should be Idle)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.Idle.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with OnboardingAutoApproved reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.OnboardingAutoApproved.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithBothAutoApprovalSettingsEnabledButOnlyOrgLeaderMatches_TestAsync()
        {
            // Setup context
            var userUpn = "user@domain.com";
            var userId = Guid.NewGuid().ToString();
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, userUpn),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup both auto-approval settings to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "true" });

            // Setup user with onPremisesImmutableId matching manager ID
            var userOnPremisesImmutableId = "12345";
            _graphGroupRepository.Setup(x => x.GetUserWithOnPremisesImmutableIdAsync(userId, It.IsAny<Guid?>()))
                                .ReturnsAsync(new AzureADUser { 
                                    UserPrincipalName = userUpn, 
                                    OnPremisesImmutableId = userOnPremisesImmutableId 
                                });

            // Setup sync job with single SqlMembership query where manager ID matches user's onPremisesImmutableId
            _newSyncJob.Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{userOnPremisesImmutableId}}}}}}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was auto-approved (status should be Idle)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.Idle.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with OnboardingAutoApproved reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.OnboardingAutoApproved.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithAutoApprovalErrorDuringSettingsRetrievalTestAsync()
        {
            // Setup context
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup settings repository to throw exception
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ThrowsAsync(new Exception("Database error"));

            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "false" });

            // Setup sync job with GroupMembership query
            var groupId = Guid.NewGuid();
            _newSyncJob.Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was NOT auto-approved due to settings error (status should be PendingReview)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithAutoApprovalErrorDuringGraphAPICallTestAsync()
        {
            // Setup context
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup auto-approval setting to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });

            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "false" });

            // Setup Graph API to throw exception
            var groupId = Guid.NewGuid();
            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                .ThrowsAsync(new Exception("Graph API error"));

            // Setup sync job with GroupMembership query
            _newSyncJob.Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was NOT auto-approved due to Graph API error (status should be PendingReview)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithAutoApprovalInvalidJSONQueryTestAsync()
        {
            // Setup context
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup auto-approval setting to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });

            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "false" });

            // Setup required mocks for job creation
            var destinationGuid = Guid.NewGuid();
            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                                .ReturnsAsync(true);

            _graphGroupRepository.Setup(x => x.GetGroupEmailAsync(It.IsAny<Guid>()))
                                .ReturnsAsync("test@example.com");

            _databaseSyncJobsRepository.Setup(x => x.CreateSyncJobAsync(It.IsAny<SyncJob>()))
                                      .ReturnsAsync(Guid.NewGuid());

            // Setup sync job with valid destination but invalid JSON query
            _newSyncJob = new NewSyncJobDTO
            {
                Destination = $"[{{\"value\":{{\"objectId\":\"{destinationGuid}\"}},\"type\":\"GroupMembership\"}}]",
                Status = SyncStatus.PendingReview.ToString(),
                Period = 24,
                Query = "invalid json {", // Invalid JSON query
                Requestor = "user@domain.com",
                StartDate = DateTime.UtcNow.AddDays(-1).ToString(),
                ThresholdPercentageForAdditions = 100,
                ThresholdPercentageForRemovals = 20
            };

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as ObjectResult;
            
            Assert.IsNotNull(result);
            Assert.AreEqual(500, result.StatusCode);
            
            // Verify that the sync job repository was not called due to the JSON parsing exception
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.IsAny<SyncJob>()), Times.Never);
            
            // Verify that the sync job change repository was not called due to the JSON parsing exception
            _syncJobChangeRepository.Verify(x => x.Save(It.IsAny<SyncJobChange>()), Times.Never);
        }

        [TestMethod]
        public async Task PostJobWithAutoApprovalEmptyQueryArrayTestAsync()
        {
            // Setup context
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup auto-approval setting to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "true" });

            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "false" });

            // Setup required mocks for job creation
            var destinationGuid = Guid.NewGuid();
            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                                .ReturnsAsync(true);

            _graphGroupRepository.Setup(x => x.GetGroupEmailAsync(It.IsAny<Guid>()))
                                .ReturnsAsync("test@example.com");

            _databaseSyncJobsRepository.Setup(x => x.CreateSyncJobAsync(It.IsAny<SyncJob>()))
                                      .ReturnsAsync(Guid.NewGuid());

            // Setup sync job with valid destination but empty query array
            _newSyncJob = new NewSyncJobDTO
            {
                Destination = $"[{{\"value\":{{\"objectId\":\"{destinationGuid}\"}},\"type\":\"GroupMembership\"}}]",
                Status = SyncStatus.PendingReview.ToString(),
                Period = 24,
                Query = "[]", // Empty query array
                Requestor = "user@domain.com",
                StartDate = DateTime.UtcNow.AddDays(-1).ToString(),
                ThresholdPercentageForAdditions = 100,
                ThresholdPercentageForRemovals = 20
            };

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was NOT auto-approved due to empty query (status should be PendingReview)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithOrgLeaderAutoApprovalInvalidOnPremisesImmutableIdTestAsync()
        {
            // Setup context
            var userUpn = "user@domain.com";
            var userId = Guid.NewGuid().ToString();
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, userUpn),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup org leader auto-approval setting to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "false" });

            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "true" });

            // Setup user with non-numeric onPremisesImmutableId
            _graphGroupRepository.Setup(x => x.GetUserWithOnPremisesImmutableIdAsync(userId, It.IsAny<Guid?>()))
                                .ReturnsAsync(new AzureADUser { 
                                    UserPrincipalName = userUpn, 
                                    OnPremisesImmutableId = "abc123" // Non-numeric value
                                });

            // Setup sync job with single SqlMembership query
            var managerId = "12345";
            _newSyncJob.Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}}}}}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was NOT auto-approved due to invalid onPremisesImmutableId (status should be PendingReview)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithOrgLeaderAutoApprovalUserLookupErrorTestAsync()
        {
            // Setup context
            var userUpn = "user@domain.com";
            var userId = Guid.NewGuid().ToString();
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, userUpn),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup org leader auto-approval setting to enabled
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "false" });

            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "true" });

            // Setup user lookup to throw exception
            _graphGroupRepository.Setup(x => x.GetUserWithOnPremisesImmutableIdAsync(userId, It.IsAny<Guid?>()))
                                .ThrowsAsync(new Exception("User lookup error"));

            // Setup sync job with single SqlMembership query
            var managerId = "12345";
            _newSyncJob.Query = $"[{{\"type\":\"SqlMembership\",\"source\":{{\"manager\":{{\"id\":{managerId}}}}}}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was created but NOT auto-approved due to the user lookup error
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular onboarding reason (not auto-approved)
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithAutoApprovalSettingNullValueTestAsync()
        {
            // Setup context
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup settings repository to return null setting
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync((Setting)null);

            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync((Setting)null);

            // Setup sync job with GroupMembership query
            var groupId = Guid.NewGuid();
            _newSyncJob.Query = $"[{{\"type\":\"GroupMembership\",\"source\":\"{groupId}\"}}]";

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            
            Assert.IsNotNull(result);
            
            // Verify that the job was NOT auto-approved due to null settings (status should be PendingReview)
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job => 
                job.Status == SyncStatus.PendingReview.ToString())), Times.Once);
            
            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change => 
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithPendingConfigurationEnabledTestAsync()
        {
            // Setup context
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup pending configuration to enabled
            _pendingConfigurationConfig.Setup(x => x.PendingConfigurationIsEnabled).Returns(true);

            // Setup auto-approval settings to disabled (ensure they don't interfere)
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "false" });

            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "false" });

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;

            Assert.IsNotNull(result);

            // Verify that the job was set to PendingConfiguration status
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job =>
                job.Status == SyncStatus.PendingConfiguration.ToString())), Times.Once);

            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change =>
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);

            // Verify that a message was sent to the service bus queue for configuration
            _serviceBusQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once);

            // Verify sending of service bus message was logged
            _loggingRepository.Verify(x => x.LogMessageAsync(
                It.Is<LogMessage>(log => log.Message.Contains("to configuration queue")),
                It.IsAny<VerbosityLevel>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public async Task PostJobWithPendingConfigurationEnabledServiceBusExceptionTestAsync()
        {
            // Setup context
            _context = CreateHttpContext(new List<Claim>
            {
                new Claim(ClaimTypes.Name, "user@domain.com"),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
            });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            // Setup pending configuration to enabled
            _pendingConfigurationConfig.Setup(x => x.PendingConfigurationIsEnabled).Returns(true);

            // Setup auto-approval settings to disabled (ensure they don't interfere)
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "false" });

            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled, SettingValue = "false" });

            _serviceBusQueueRepository.Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()))
                                      .ThrowsAsync(new Exception("Service Bus error"));

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _titlesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object,
                                                 _pendingConfigurationConfig.Object,
                                                 _serviceBusQueueRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _patchJobsHandler, _postJobHandler, _getJobDetailsHandler, _postResetRequestHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);

            Assert.IsInstanceOfType(response, typeof(ObjectResult));
            var result = response as ObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual(StatusCodes.Status500InternalServerError, result.StatusCode);

            // Verify that the job was set to PendingConfiguration status
            _databaseSyncJobsRepository.Verify(x => x.CreateSyncJobAsync(It.Is<SyncJob>(job =>
                job.Status == SyncStatus.PendingConfiguration.ToString())), Times.Once);

            // Verify that the sync job change was saved with regular Onboarding reason
            _syncJobChangeRepository.Verify(x => x.Save(It.Is<SyncJobChange>(change =>
                change.ChangeReason == SyncJobChangeReason.Onboarding.ToString())), Times.Once);

            // Verify that an attempt to send a message to the service bus queue for configuration was made
            _serviceBusQueueRepository.Verify(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>()), Times.Once);
        }
    }
}

