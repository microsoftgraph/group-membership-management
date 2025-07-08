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
using Models.SyncJobChange;
using Moq;
using Repositories.Contracts;
using Repositories.TeamsChannel;
using Services.Messages.Responses;
using Services.WebApi;
using Services.WebApi.Contracts;
using System.Data;
using System.Net;
using System.Security.Claims;
using WebApi.Controllers.v1.Jobs;
using WebApi.Models;
using WebApi.Models.Responses;
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
        private Mock<IDatabaseDestinationAttributesRepository> _destinationAttributesRepository = null!;
        private Mock<GraphServiceClient> _graphServiceClient = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private Mock<ITeamsChannelRepository> _teamsChannelRepository = null!;
        private Mock<IDatabaseSettingsRepository> _databaseSettingsRepository = null!;
        private ODataQueryOptions<SyncJob> _odataQueryOptions = null!;
        private Mock<IHttpContextAccessor> _httpContextAccessor = null!;
        private PostOperationHandler _postResetRequestHandler = null!;
        private Mock<IServiceStatusRepository> _serviceStatusRepository = null!;
        private Mock<IOperationsTaskQueue> _backgroundTaskService = null!;

        [TestInitialize]
        public void Initialize()
        {
            _groups = new List<AzureADGroup>();
            _context = new DefaultHttpContext();
            _requestAdapter = new Mock<IRequestAdapter>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _databaseSyncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
            _syncJobChangeRepository = new Mock<ISyncJobChangeRepository>();
            _destinationAttributesRepository = new Mock<IDatabaseDestinationAttributesRepository>();
            _httpContextAccessor = new Mock<IHttpContextAccessor>();

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

            // Setup default auto-approval setting to false
            _databaseSettingsRepository.Setup(x => x.GetSettingByKeyAsync(SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled))
                                      .ReturnsAsync(new Setting { SettingKey = SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled, SettingValue = "false" });

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                    .ReturnsAsync(() => _groups);

            _graphGroupRepository.Setup(x => x.GetUserByUpnOrIdAsync(It.IsAny<string>(), false))
                                    .ReturnsAsync(() => new AzureADUser { UserPrincipalName = "upn" } );

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
                                                 _httpContextAccessor.Object);

            _patchJobsHandler = new PatchJobsHandler(_loggingRepository.Object,
                                                 _databaseSyncJobsRepository.Object,
                                                 _syncJobChangeRepository.Object);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object);

            _getJobDetailsHandler = new GetJobDetailsHandler(_loggingRepository.Object,
                                                _databaseSyncJobsRepository.Object,
                                                _syncJobChangeRepository.Object,
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
                                     _httpContextAccessor.Object);

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
                                     _httpContextAccessor.Object);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _destinationAttributesRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object);

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
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object);

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
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object);

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
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object);

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
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object);

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
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object);

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
                                                 _syncJobChangeRepository.Object);

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
            _databaseSyncJobsRepository.Verify(x => x.BulkApproveSyncJobsAsync(It.IsAny<List<string>>()), Times.Once);
            _syncJobChangeRepository.Verify(x => x.BulkSaveAsync(It.IsAny<IEnumerable<SyncJobChange>>()), Times.Once);
        }

        [TestMethod]
        public async Task BulkApproveExceptionTestAsync()
        {
            _databaseSyncJobsRepository.Setup(x => x.BulkApproveSyncJobsAsync(It.IsAny<List<string>>()))
                                        .ThrowsAsync(new Exception());

            var response = await _jobsController.BulkApproveJobsAsync(_syncJobIds.ToArray());

            Assert.IsInstanceOfType(response, typeof(ActionResult<int>));

            var statusCodeResult = response.Result as StatusCodeResult;

            Assert.IsNotNull(statusCodeResult);
            Assert.AreEqual((int)HttpStatusCode.InternalServerError, statusCodeResult.StatusCode);
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
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object);

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
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object);

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
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object);

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
                                                 _graphGroupRepository.Object,
                                                 _loggingRepository.Object,
                                                 _syncJobChangeRepository.Object,
                                                 _databaseSettingsRepository.Object);

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
    }
}

