// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;
using MockQueryable.Moq;
using Models;
using Moq;
using Repositories.Contracts;
using Services.WebApi;
using System.Data;
using System.Security.Claims;
using WebApi.Controllers.v1.Jobs;
using WebApi.Models;
using WebApi.Models.DTOs;
using SyncJob = Models.SyncJob;
using SyncJobDetails = WebApi.Models.DTOs.SyncJobDetails;

namespace Services.Tests
{
    [TestClass]
    public class JobDetailsControllerTests
    {
        private SyncJob _jobEntity = null!;
        private JobDetailsController _jobDetailsController = null!;
        private GetJobDetailsHandler _getJobDetailsHandler = null!;
        private PatchJobHandler _patchJobHandler = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private bool _isGroupOwner = true;
        private Mock<IHttpContextAccessor> _httpContextAccessor = null!;

        [TestInitialize]
        public void Initialize()
        {
            _httpContextAccessor = new Mock<IHttpContextAccessor>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();

            _graphGroupRepository = new Mock<IGraphGroupRepository>();

            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.IsAny<Guid>()))
                                    .ReturnsAsync(() => _isGroupOwner);

            _jobEntity = new SyncJob
            {
                Id = Guid.NewGuid(),
                Status = ((SyncStatus)Random.Shared.Next(1, 15)).ToString(),
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
            };

            _syncJobRepository.Setup(x => x.GetSyncJobAsync(It.IsAny<Guid>()))
                              .ReturnsAsync(() => _jobEntity);

            _syncJobRepository.Setup(x => x.GetSyncJobs(It.IsAny<bool>()))
                              .Returns(() =>
                              {
                                  var jobs = new List<SyncJob> { _jobEntity };
                                  return jobs.BuildMock();
                              });

            _getJobDetailsHandler = new GetJobDetailsHandler(_loggingRepository.Object,
                                                             _syncJobRepository.Object,
                                                             _graphGroupRepository.Object,
                                                             _httpContextAccessor.Object);

            _patchJobHandler = new PatchJobHandler(_loggingRepository.Object,
                                                   _graphGroupRepository.Object,
                                                   _syncJobRepository.Object);

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _patchJobHandler);
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
        [DataRow(Roles.TENANT_READER)]
        [DataRow(Roles.TENANT_ADMINISTRATOR)]
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
            Assert.IsNotNull(job.Source);
        }

        [TestMethod]
        [DataRow(Roles.TENANT_READER)]
        [DataRow(Roles.TENANT_ADMINISTRATOR)]
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

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _patchJobHandler);

            var response = await _jobDetailsController.GetJobDetailsAsync(Guid.NewGuid());
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var job = result.Value as SyncJobDetails;

            Assert.IsNotNull(job);
            Assert.AreEqual("example@microsoft.com (Not an Owner)", job.Requestor);
        }

        [TestMethod]
        public async Task PatchJobWithInvalidStatus()
        {
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _patchJobHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim> { new Claim(ClaimTypes.Name, "user@domain.com") })
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, "InvalidStatus");

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as BadRequestObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(400, result.StatusCode);
            Assert.AreEqual("StatusIsNotValid", result.Value);
        }

        [TestMethod]
        public async Task PatchJobWithEmptyStatus()
        {
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _patchJobHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim> { new Claim(ClaimTypes.Name, "user@domain.com") })
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, null);

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as BadRequestObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(400, result.StatusCode);
            Assert.AreEqual("StatusIsRequired", result.Value);
        }

        [TestMethod]
        public async Task PatchJobStatusWhenJobIsInProgress()
        {
            _jobEntity.Status = SyncStatus.InProgress.ToString();

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _patchJobHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim> { new Claim(ClaimTypes.Name, "user@domain.com") })
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
        }

        [TestMethod]
        public async Task PatchNonExistentJob()
        {
            _jobEntity = null;

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _patchJobHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim> { new Claim(ClaimTypes.Name, "user@domain.com") })
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, "Idle");

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as NotFoundResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(404, result.StatusCode);
        }

        [TestMethod]
        public async Task PatchJobWhenIsNotOwnerOfTheGroup()
        {
            _isGroupOwner = false;
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _patchJobHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim> { new Claim(ClaimTypes.Name, "user@domain.com") })
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, "CustomerPaused");

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as ForbidResult;

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task PatchJobWhenIsNotOwnerOfTheGroupButIsAdmin()
        {
            _isGroupOwner = false;
            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _patchJobHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.TENANT_ADMINISTRATOR)
                })
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, "CustomerPaused");

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as OkResult;

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task PatchJobWhenIsAnOwner()
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
                    new Claim(ClaimTypes.Role, "UserRole"),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
                });

            _httpContextAccessor.Setup(x => x.HttpContext).Returns(context);

            _jobDetailsController = new JobDetailsController(_getJobDetailsHandler, _patchJobHandler)
            {
                ControllerContext = CreateControllerContext(context)
            };

            var patchDocument = new JsonPatchDocument<SyncJobPatch>();
            patchDocument.Replace(x => x.Status, "CustomerPaused");

            var response = await _jobDetailsController.UpdateSyncJobAsync(Guid.NewGuid(), patchDocument);
            var result = response as OkResult;

            Assert.IsNotNull(result);
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

