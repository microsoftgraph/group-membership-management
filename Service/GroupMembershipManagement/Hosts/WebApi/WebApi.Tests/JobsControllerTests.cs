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
using Moq;
using Repositories.Contracts;
using System.Data;
using System.Security.Claims;
using WebApi.Controllers.v1.Jobs;
using WebApi.Models;
using WebApi.Models.Responses;
using NewSyncJobDTO = WebApi.Models.DTOs.NewSyncJob;

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
        private List<AzureADGroup> _groups = null!;
        private JobsController _jobsController = null!;
        private GetJobsHandler _getJobsHandler = null!;
        private PostJobHandler _postJobHandler = null!;
        private TelemetryClient _telemetryClient = null!;
        private Mock<IRequestAdapter> _requestAdapter = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _databaseSyncJobsRepository = null!;
        private Mock<GraphServiceClient> _graphServiceClient = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private ODataQueryOptions<SyncJob> _odataQueryOptions = null!;
        private Mock<IHttpContextAccessor> _httpContextAccessor = null!;

        [TestInitialize]
        public void Initialize()
        {
            _groups = new List<AzureADGroup>();
            _context = new DefaultHttpContext();
            _requestAdapter = new Mock<IRequestAdapter>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _databaseSyncJobsRepository = new Mock<IDatabaseSyncJobsRepository>();
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

            _graphGroupRepository.Setup(x => x.GetGroupsAsync(It.IsAny<List<Guid>>()))
                                    .ReturnsAsync(() => _groups);

            var telemetryConfiguration = new TelemetryConfiguration();
            _telemetryClient = new TelemetryClient(telemetryConfiguration);

            _groupTypes = new List<string>
            {
                "Microsoft 365",
                "Security",
                "Mail enabled security",
                "Distribution"
            };

            _jobEntities = Enumerable.Range(0, _jobCount).Select(x => new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = ((SyncStatus)Random.Shared.Next(1, 15)).ToString(),
                TargetOfficeGroupId = Guid.NewGuid(),
                LastSuccessfulRunTime = DateTime.UtcNow.AddHours(-4),
                LastSuccessfulStartTime = DateTime.UtcNow.AddHours(-5),
                StartDate = DateTime.UtcNow.AddMonths(-1),
                ThresholdPercentageForAdditions = 10,
                ThresholdPercentageForRemovals = 10,
                Period = 6,
                StatusDetails = new Status
                {
                    Id = Guid.Parse("AC3604F9-5869-EE11-9937-6045BDE913DD"),
                    Name = SyncStatus.Idle.ToString(),
                    SortPriority = 1000
                }
            }).ToList();

            _jobEntities.ForEach(x =>
            {
                _groups.Add(new AzureADGroup
                {
                    ObjectId = x.TargetOfficeGroupId,
                    Type = _groupTypes[Random.Shared.Next(0, _groupTypes.Count)]
                });
            });

            _newSyncJob = new NewSyncJobDTO
            {
                Id = Guid.NewGuid(),
                Destination = "[{\"value\":{\"objectId\":\"e2e78850-ed7c-49dc-91f2-4cb57a608504\"},\"type\":\"GroupMembership\"}]",
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
                .ReturnsAsync(_newSyncJob.Id);

            _getJobsHandler = new GetJobsHandler(_loggingRepository.Object,
                                                 _databaseSyncJobsRepository.Object,
                                                 _graphGroupRepository.Object,
                                                 _httpContextAccessor.Object);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _loggingRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _postJobHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };
        }

        [TestMethod]
        [DataRow(Roles.TENANT_READER)]
        [DataRow(Roles.TENANT_ADMINISTRATOR)]
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

            var jobs = result.Value as GetJobsModel;

            Assert.IsNotNull(jobs);
            Assert.AreEqual(_jobCount, jobs.Count);
            Assert.AreEqual(_jobCount, jobs.Select(x => x.TargetGroupId).Distinct().Count());
            Assert.IsTrue(jobs.All(x => x.SyncJobId.ToString() != null));
            Assert.IsTrue(jobs.All(x => x.EstimatedNextRunTime == x.LastSuccessfulRunTime.AddHours(6)));
            Assert.IsTrue(jobs.All(x => x.Status != null));
            Assert.IsTrue(jobs.All(x => x.TargetGroupType != null));
        }

        [TestMethod]
        [DataRow(Roles.TENANT_READER)]
        public async Task GetJobsTestWithGraphAPIFailureAsync(string role)
        {
            _context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, role)
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

            _jobsController = new JobsController(_getJobsHandler, _postJobHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.GetJobsAsync(_odataQueryOptions);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var jobs = result.Value as GetJobsModel;

            Assert.IsNotNull(jobs);
            Assert.AreEqual(_jobCount, jobs.Count);
            Assert.AreEqual(_jobCount, jobs.Select(x => x.TargetGroupId).Distinct().Count());
            Assert.IsTrue(jobs.All(x => x.SyncJobId.ToString() != null));
            Assert.IsTrue(jobs.All(x => x.EstimatedNextRunTime == x.LastSuccessfulRunTime.AddHours(6)));
            Assert.IsTrue(jobs.All(x => x.Status != null));
            Assert.IsTrue(jobs.All(x => x.TargetGroupType == null));

        }

        [TestMethod]
        public async Task PostJobSuccessfullyTestAsync()
        {
            _context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.TENANT_ADMINISTRATOR),

                }); 

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            _getJobsHandler = new GetJobsHandler(
                                     _loggingRepository.Object,
                                     _databaseSyncJobsRepository.Object,
                                     _graphGroupRepository.Object,
                                     _httpContextAccessor.Object);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _loggingRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _postJobHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as CreatedResult;
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task PostJobWhenNotAllowedTestAsync()
        {
            _context = CreateHttpContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.TENANT_READER),

                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(_context);

            _getJobsHandler = new GetJobsHandler(
                                     _loggingRepository.Object,
                                     _databaseSyncJobsRepository.Object,
                                     _graphGroupRepository.Object,
                                     _httpContextAccessor.Object);

            _postJobHandler = new PostJobHandler(_databaseSyncJobsRepository.Object,
                                                 _loggingRepository.Object);

            _jobsController = new JobsController(_getJobsHandler, _postJobHandler);
            _jobsController.ControllerContext = new ControllerContext
            {
                HttpContext = _context
            };

            var response = await _jobsController.PostJobAsync(_newSyncJob);
            var result = response as ForbidResult;
            Assert.IsNotNull(result);
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
    }
}

