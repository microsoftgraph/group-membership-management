// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Moq;
using Repositories.Contracts;
using WebApi.Controllers.v1.Destination;
using Microsoft.AspNetCore.Http;
using WebApi.Models.Responses;
using Models;
using Microsoft.Extensions.Options;
using Common.DependencyInjection;
using System.Security.Claims;
using WebApi.Models;
using NewGroupDTO = WebApi.Models.DTOs.NewGroup;
using Channel = Microsoft.Graph.Models.Channel;
using Repositories.Contracts.InjectConfig;
using Services.Messages.Responses;
using Models.Entities;
using Services.Messages.Requests;

namespace Services.Tests
{
    [TestClass]
    public class DestinationControllerTests
    {
        private int _destinationCount = 100;
        private Guid _validDestinationId = Guid.NewGuid();
        private List<string> _groupTypes = null!;
        private List<AzureADGroup> _destinations = null!;
        private List<string> _expectedEndpoints = null!;
        private List<Channel> _channels = null!;
        private HttpContext _context = null!;
        private DestinationController _destinationController = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private Mock<ITeamsChannelRepository> _teamsChannelRepository = null!;
        private Mock<ITeamsChannelConfig> _teamsChannelConfig = null!;
        private Mock<IDatabaseSyncJobsRepository> _syncJobRepository = null!;
        private SearchGroupsHandler _searchGroupsHandler = null!;
        private SearchChannelsHandler _searchChannelsHandler = null!;
        private GetGroupEndpointsHandler _getGroupEndpointsHandler = null!;
        private GetGroupOwnersHandler _getGroupOwnersHandler = null!;
        private GetGroupOnboardingStatusHandler _getGroupOnboardingStatusHandler = null!;
        private GetChannelOnboardingStatusHandler _getChannelOnboardingStatusHandler = null!;
        private Mock<IOptions<GraphCredentials>> _graphCredentials = null!;
        private PostGroupHandler _postGroupHandler = null!;
        private NewGroupDTO _newGroup = null!;
        private readonly string _serviceAccountUserName = "alias@domain.com";
        private readonly string _teamsChannelAppRegistrationName = "<sol>-TeamsChannel-<env>";

        [TestInitialize]
        public void Initialize()
        {
            _context = new DefaultHttpContext();
            _destinations = new List<AzureADGroup>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _syncJobRepository = new Mock<IDatabaseSyncJobsRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _teamsChannelRepository = new Mock<ITeamsChannelRepository>();
            _teamsChannelConfig = new Mock<ITeamsChannelConfig>();
            _searchGroupsHandler = new SearchGroupsHandler(_loggingRepository.Object, _graphGroupRepository.Object);
            _searchChannelsHandler = new SearchChannelsHandler(_loggingRepository.Object, _teamsChannelRepository.Object);
            _getGroupEndpointsHandler = new GetGroupEndpointsHandler(_loggingRepository.Object, _graphGroupRepository.Object);
            _getGroupOwnersHandler = new GetGroupOwnersHandler(_loggingRepository.Object, _graphGroupRepository.Object);
            _postGroupHandler = new PostGroupHandler(_loggingRepository.Object, _graphGroupRepository.Object);
            _graphCredentials = new Mock<IOptions<GraphCredentials>>();
            var testGraphCredentials = new GraphCredentials
            {
                ClientId = "00000003-0000-0000-c000-000000000000",
                AuthenticationType = AuthenticationType.ClientSecret,
                AppRegistrationName = "<sol>-Graph-<env>",
                UAMIName = "<sol>-identity-<env>-graph"
            };

            _teamsChannelConfig.Setup(x => x.TeamsChannelServiceAccountUsername)
                               .Returns(_serviceAccountUserName);
            _teamsChannelConfig.Setup(x => x.TeamsChannelAppRegistrationName)
                               .Returns(_teamsChannelAppRegistrationName);


            _graphCredentials.Setup(gc => gc.Value).Returns(testGraphCredentials);
            _getGroupOnboardingStatusHandler = new GetGroupOnboardingStatusHandler(_loggingRepository.Object,
                                                                                   _graphGroupRepository.Object,
                                                                                   _syncJobRepository.Object,
                                                                                   _graphCredentials.Object);
            _getChannelOnboardingStatusHandler = new GetChannelOnboardingStatusHandler(_loggingRepository.Object,
                                                                                    _graphGroupRepository.Object,
                                                                                    _teamsChannelRepository.Object,
                                                                                    _teamsChannelConfig.Object,
                                                                                    _syncJobRepository.Object,
                                                                                    _graphCredentials.Object);

            _destinationController = new DestinationController(
                _searchGroupsHandler,
                _searchChannelsHandler,
                _getGroupEndpointsHandler,
                _getGroupOwnersHandler,
                _getGroupOnboardingStatusHandler,
                _getChannelOnboardingStatusHandler,
                _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };

            _newGroup = new NewGroupDTO
            {
                UserIdentity = Guid.NewGuid(),
                GroupAlias = "",
                GroupName = ""
            };
            _groupTypes = new List<string>
            {
                "Microsoft 365",
                "Security",
                "Mail enabled security",
                "Distribution"
            };

            foreach (var index in Enumerable.Range(0, _destinationCount))
            {
                var destination = new AzureADGroup
                {
                    ObjectId = Guid.NewGuid(),
                    Type = _groupTypes[Random.Shared.Next(0, _groupTypes.Count)]
                };

                var destinationName = $"Test Destination {index}";
                _destinations.Add(destination);
            }

            _expectedEndpoints = new List<string> { "Yammer", "Outlook" };

            _channels = [new Channel { Id = "TestId", DisplayName = "TestName" }];

            _graphGroupRepository.Setup(x => x.SearchDestinationsAsync(It.IsAny<string>())).ReturnsAsync(() => _destinations);
            _graphGroupRepository.Setup(x => x.IsAppIDOwnerOfGroup(It.IsAny<string>(), It.Is<Guid>(g => g == _validDestinationId))).ReturnsAsync(true);
            _graphGroupRepository.Setup(x => x.GetGroupEndpointsAsync(It.IsAny<Guid>())).ReturnsAsync(_expectedEndpoints);

            _teamsChannelRepository.Setup(x => x.SearchTeamsChannelsAsync(It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync(() => _channels);

            var syncJob = new SyncJob
            {
                Id = Guid.NewGuid(),
                Query = "[{ \"type\": \"GroupMembership\", \"sources\": [\"da144736-962b-4879-a304-acd9f5221e78\"]}]",
                Status = "Idle",
                Period = 12,
                MembershipType = "GroupMembership",
                Group = new Group { GroupId = Guid.NewGuid() }
            };
            _syncJobRepository.Setup(x => x.GetSyncJobByObjectIdAsync(It.IsAny<Guid>())).ReturnsAsync(syncJob);

        }

        [TestMethod]
        public async Task SearchGroupsTestAsync()
        {
            var response = await _destinationController.SearchGroupsAsync("Test");
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var destinations = result.Value as GetDestinationsModel;
            Assert.IsNotNull(destinations);
            Assert.AreEqual(_destinationCount, destinations.Count);
        }

        [TestMethod]
        public async Task SearchChannelsTestAsync()
        {
            var response = await _destinationController.SearchChannelsAsync(new Guid(), "TestChannel");
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var channels = result.Value as GetChannelsModel;
            Assert.IsNotNull(channels);
            Assert.AreEqual(1, channels.Count);
        }

        [TestMethod]
        public async Task GetGroupEndpointsTestAsync()
        {
            var response = await _destinationController.GetGroupEndpointsAsync(_validDestinationId);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result?.Value);

            var actualEndpoints = result.Value;
            Assert.IsNotNull(actualEndpoints);
            Assert.AreEqual(_expectedEndpoints, actualEndpoints);
        }

        [TestMethod]
        public async Task GetGroupAlreadyOnboardedStatusAsync()
        {
            var response = await _destinationController.GetGroupOnboardingStatusAsync(_validDestinationId);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result?.Value);

            var onboardingStatus = result.Value as GetOnboardingStatusResponse;
            Assert.IsNotNull(onboardingStatus);
            Assert.AreEqual(OnboardingStatus.Onboarded, onboardingStatus.Status);
        }

        [TestMethod]
        public async Task GetGroupReadyForOnboardingStatusAsync()
        {
            Guid groupNotOnboarded = Guid.NewGuid();
            _syncJobRepository.Setup(x => x.GetSyncJobByObjectIdAsync(It.IsAny<Guid>())).ReturnsAsync((SyncJob)null);
            _graphGroupRepository.Setup(x => x.IsAppIDOwnerOfGroup(It.IsAny<string>(), It.Is<Guid>(g => g == groupNotOnboarded))).ReturnsAsync(true);
            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.Is<Guid>(g => g == groupNotOnboarded))).ReturnsAsync(true);

            var response = await _destinationController.GetGroupOnboardingStatusAsync(groupNotOnboarded);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result?.Value);

            var onboardingStatus = result.Value as GetOnboardingStatusResponse;
            Assert.IsNotNull(onboardingStatus);
            Assert.AreEqual(OnboardingStatus.ReadyForOnboarding, onboardingStatus.Status);
        }

        [TestMethod]
        public async Task GetGroupAppIdNotOwnerStatusAsync()
        {
            Guid groupNotOnboarded = Guid.NewGuid();
            _syncJobRepository.Setup(x => x.GetSyncJobByObjectIdAsync(It.IsAny<Guid>())).ReturnsAsync((SyncJob)null);
            _graphGroupRepository.Setup(x => x.IsAppIDOwnerOfGroup(It.IsAny<string>(), It.Is<Guid>(g => g == groupNotOnboarded))).ReturnsAsync(false);
            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.Is<Guid>(g => g == groupNotOnboarded))).ReturnsAsync(true);

            var response = await _destinationController.GetGroupOnboardingStatusAsync(groupNotOnboarded);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result?.Value);

            var onboardingStatus = result.Value as GetOnboardingStatusResponse;
            Assert.IsNotNull(onboardingStatus);
            Assert.AreEqual(OnboardingStatus.GmmNotOwner, onboardingStatus.Status);
            Assert.IsNotNull(onboardingStatus.AdditionalDetails);
            Assert.IsNotNull(onboardingStatus.AdditionalDetails["owner"]);
            Assert.AreEqual(_graphCredentials.Object.Value.AppRegistrationName, onboardingStatus.AdditionalDetails["owner"]);
        }

        [TestMethod]
        public async Task GetGroupUserNotOwnerStatusAsync()
        {
            _destinationController = new DestinationController(_searchGroupsHandler, _searchChannelsHandler, _getGroupEndpointsHandler, _getGroupOwnersHandler, _getGroupOnboardingStatusHandler, _getChannelOnboardingStatusHandler, _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.JOB_OWNER_WRITER),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };

            Guid groupNotOnboarded = Guid.NewGuid();
            _syncJobRepository.Setup(x => x.GetSyncJobByObjectIdAsync(It.IsAny<Guid>())).ReturnsAsync((SyncJob)null);
            _graphGroupRepository.Setup(x => x.IsAppIDOwnerOfGroup(It.IsAny<string>(), It.Is<Guid>(g => g == groupNotOnboarded))).ReturnsAsync(true);
            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.Is<Guid>(g => g == groupNotOnboarded))).ReturnsAsync(false);

            var response = await _destinationController.GetGroupOnboardingStatusAsync(groupNotOnboarded);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result?.Value);

            var onboardingStatus = result.Value as GetOnboardingStatusResponse;
            Assert.IsNotNull(onboardingStatus);
            Assert.AreEqual(OnboardingStatus.UserNotOwner, onboardingStatus.Status);
        }

        [TestMethod]
        public async Task GetGroupOnboardingStatusWhenClaimIsNotFoundAsync()
        {
            _destinationController = new DestinationController(_searchGroupsHandler, _searchChannelsHandler, _getGroupEndpointsHandler, _getGroupOwnersHandler, _getGroupOnboardingStatusHandler, _getChannelOnboardingStatusHandler, _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.JOB_OWNER_WRITER),
                })
            };

            Guid groupId = Guid.NewGuid();
            _syncJobRepository.Setup(x => x.GetSyncJobByObjectIdAsync(It.IsAny<Guid>())).ReturnsAsync((SyncJob)null);
            _graphGroupRepository.Setup(x => x.IsAppIDOwnerOfGroup(It.IsAny<string>(), It.Is<Guid>(g => g == groupId))).ReturnsAsync(true);
            _graphGroupRepository.Setup(x => x.IsEmailRecipientOwnerOfGroupAsync(It.IsAny<string>(), It.Is<Guid>(g => g == groupId))).ReturnsAsync(false);

            var response = await _destinationController.GetGroupOnboardingStatusAsync(groupId);
            var result = response.Result as ForbidResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task GetGroupOnboardingStatusThrowsExceptionAsync()
        {
            _destinationController = new DestinationController(_searchGroupsHandler, _searchChannelsHandler, _getGroupEndpointsHandler, _getGroupOwnersHandler, _getGroupOnboardingStatusHandler, _getChannelOnboardingStatusHandler, _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.JOB_OWNER_WRITER),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };

            Guid groupNotOnboarded = Guid.NewGuid();
            _syncJobRepository.Setup(x => x.GetSyncJobByObjectIdAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("Database error"));

            var response = await _destinationController.GetGroupOnboardingStatusAsync(groupNotOnboarded);
            var result = response.Result as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(500, result?.StatusCode);
        }

        [TestMethod]
        public async Task GetChannelAlreadyOnboardedStatusAsync()
        {
            var response = await _destinationController.GetChannelOnboardingStatusAsync(_validDestinationId, "TestChannelId");
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result?.Value);

            var onboardingStatus = result.Value as GetOnboardingStatusResponse;
            Assert.IsNotNull(onboardingStatus);
            Assert.AreEqual(OnboardingStatus.Onboarded, onboardingStatus.Status);
        }

        [TestMethod]
        public async Task GetChannelReadyForOnboardingStatusAsync()
        {
            Guid teamId = Guid.NewGuid();
            string channelId = "TestChannelId";
            _syncJobRepository.Setup(x => x.GetSyncJobByObjectIdAsync(It.IsAny<Guid>())).ReturnsAsync((SyncJob)null);
            _teamsChannelRepository.Setup(x => x.IsServiceAccountOwnerOfChannelAsync(It.IsAny<Guid>(), It.IsAny<AzureADTeamsChannel>(), null)).ReturnsAsync(true);

            var response = await _destinationController.GetChannelOnboardingStatusAsync(teamId, channelId);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result?.Value);

            var onboardingStatus = result.Value as GetOnboardingStatusResponse;
            Assert.IsNotNull(onboardingStatus);
            Assert.AreEqual(OnboardingStatus.ReadyForOnboarding, onboardingStatus.Status);
        }

        [TestMethod]
        public async Task GetChannelServiceAccountNotOwnerStatusAsync()
        {
            Guid teamId = Guid.NewGuid();
            string channelId = "TestChannelId";
            _syncJobRepository.Setup(x => x.GetSyncJobByObjectIdAsync(It.IsAny<Guid>())).ReturnsAsync((SyncJob)null);
            _teamsChannelRepository.Setup(x => x.IsServiceAccountOwnerOfChannelAsync(It.IsAny<Guid>(), It.IsAny<AzureADTeamsChannel>(), null)).ReturnsAsync(false);

            var response = await _destinationController.GetChannelOnboardingStatusAsync(teamId, channelId);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result?.Value);

            var onboardingStatus = result.Value as GetOnboardingStatusResponse;
            Assert.IsNotNull(onboardingStatus);
            Assert.AreEqual(OnboardingStatus.GmmNotOwner, onboardingStatus.Status);
            Assert.IsNotNull(onboardingStatus.AdditionalDetails);
            Assert.IsNotNull(onboardingStatus.AdditionalDetails["owner"]);
            Assert.AreEqual(_serviceAccountUserName, onboardingStatus.AdditionalDetails["owner"]);
        }

        [TestMethod]
        public async Task GetChannelAppRegistrationNotOwnerStatusAsync()
        {
            _teamsChannelConfig.Setup(x => x.GMMHasTeamsChannelApplicationPermissions).Returns(true);

            _getChannelOnboardingStatusHandler = new GetChannelOnboardingStatusHandler(_loggingRepository.Object,
                                                                                    _graphGroupRepository.Object,
                                                                                    _teamsChannelRepository.Object,
                                                                                    _teamsChannelConfig.Object,
                                                                                    _syncJobRepository.Object,
                                                                                    _graphCredentials.Object);

            _destinationController = new DestinationController(
                                            _searchGroupsHandler,
                                            _searchChannelsHandler,
                                            _getGroupEndpointsHandler,
                                            _getGroupOwnersHandler,
                                            _getGroupOnboardingStatusHandler,
                                            _getChannelOnboardingStatusHandler,
                                            _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };

            Guid teamId = Guid.NewGuid();
            string channelId = "TestChannelId";
            _syncJobRepository.Setup(x => x.GetSyncJobByObjectIdAsync(It.IsAny<Guid>())).ReturnsAsync((SyncJob)null);
            _teamsChannelRepository.Setup(x => x.IsServiceAccountOwnerOfChannelAsync(It.IsAny<Guid>(), It.IsAny<AzureADTeamsChannel>(), null)).ReturnsAsync(false);

            var response = await _destinationController.GetChannelOnboardingStatusAsync(teamId, channelId);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result?.Value);

            var onboardingStatus = result.Value as GetOnboardingStatusResponse;
            Assert.IsNotNull(onboardingStatus);
            Assert.AreEqual(OnboardingStatus.GmmNotOwner, onboardingStatus.Status);
            Assert.IsNotNull(onboardingStatus.AdditionalDetails);
            Assert.IsNotNull(onboardingStatus.AdditionalDetails["owner"]);
            Assert.AreEqual(_teamsChannelAppRegistrationName, onboardingStatus.AdditionalDetails["owner"]);
        }

        [TestMethod]
        public async Task GetChannelUserNotOwnerStatusAsync()
        {
            _destinationController = new DestinationController(
                _searchGroupsHandler,
                _searchChannelsHandler,
                _getGroupEndpointsHandler,
                _getGroupOwnersHandler,
                _getGroupOnboardingStatusHandler,
                _getChannelOnboardingStatusHandler,
                _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.JOB_OWNER_WRITER),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };

            Guid teamId = Guid.NewGuid();
            string channelId = "TestChannelId";
            _syncJobRepository.Setup(x => x.GetSyncJobByObjectIdAsync(It.IsAny<Guid>())).ReturnsAsync((SyncJob)null);
            _teamsChannelRepository.Setup(x => x.IsServiceAccountOwnerOfChannelAsync(It.IsAny<Guid>(), It.IsAny<AzureADTeamsChannel>(), null)).ReturnsAsync(true);

            var response = await _destinationController.GetChannelOnboardingStatusAsync(teamId, channelId);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result?.Value);

            var onboardingStatus = result.Value as GetOnboardingStatusResponse;
            Assert.IsNotNull(onboardingStatus);
            Assert.AreEqual(OnboardingStatus.UserNotOwner, onboardingStatus.Status);
        }

        [TestMethod]
        public async Task GetChannelOnboardingStatusWhenClaimIsNotFoundAsync()
        {
            _destinationController = new DestinationController(_searchGroupsHandler, _searchChannelsHandler, _getGroupEndpointsHandler, _getGroupOwnersHandler, _getGroupOnboardingStatusHandler, _getChannelOnboardingStatusHandler, _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.JOB_OWNER_WRITER),
                })
            };

            Guid teamId = Guid.NewGuid();
            string channelId = "TestChannelId";
            _syncJobRepository.Setup(x => x.GetSyncJobByObjectIdAsync(It.IsAny<Guid>())).ReturnsAsync((SyncJob)null);

            var response = await _destinationController.GetChannelOnboardingStatusAsync(teamId, channelId);
            var result = response.Result as ForbidResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task GetChannelOnboardingStatusThrowsExceptionAsync()
        {
            _destinationController = new DestinationController(_searchGroupsHandler, _searchChannelsHandler, _getGroupEndpointsHandler, _getGroupOwnersHandler, _getGroupOnboardingStatusHandler, _getChannelOnboardingStatusHandler, _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.JOB_OWNER_WRITER),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };

            Guid teamId = Guid.NewGuid();
            string channelId = "TestChannelId";
            _syncJobRepository.Setup(x => x.GetSyncJobByObjectIdAsync(It.IsAny<Guid>())).ThrowsAsync(new Exception("Database error"));

            var response = await _destinationController.GetChannelOnboardingStatusAsync(teamId, channelId);
            var result = response.Result as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(500, result?.StatusCode);
        }

        [TestMethod]
        public async Task CreateGroupSucceedsAsync()
        {
            _destinationController = new DestinationController(_searchGroupsHandler, _searchChannelsHandler, _getGroupEndpointsHandler, _getGroupOwnersHandler, _getGroupOnboardingStatusHandler, _getChannelOnboardingStatusHandler, _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };

            _graphGroupRepository.Setup(x => x.CreateGroupFromUI(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync(new AzureADGroup());

            var response = await _destinationController.CreateGroupAsync(_newGroup);
            var result = response.Result as OkObjectResult;
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);
            Assert.AreEqual(200, result?.StatusCode);
        }

        [TestMethod]
        public async Task CreateGroupThrowsExceptionAsync()
        {
            _destinationController = new DestinationController(_searchGroupsHandler, _searchChannelsHandler, _getGroupEndpointsHandler, _getGroupOwnersHandler, _getGroupOnboardingStatusHandler, _getChannelOnboardingStatusHandler, _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };

            _graphGroupRepository.Setup(x => x.CreateGroupFromUI(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>())).ThrowsAsync(new Exception("Graph error"));

            var response = await _destinationController.CreateGroupAsync(_newGroup);
            var result = response.Result as ObjectResult;
            Assert.IsNotNull(result);
            Assert.AreEqual(500, result?.StatusCode);
        }

        [TestMethod]
        public async Task GetGroupOwnersSucceedsAsync()
        {
            _destinationController = new DestinationController(_searchGroupsHandler, _searchChannelsHandler, _getGroupEndpointsHandler, _getGroupOwnersHandler, _getGroupOnboardingStatusHandler, _getChannelOnboardingStatusHandler, _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };

            var groupId = Guid.NewGuid();
            var expectedGroupOwners = new List<AzureADUser>
            {
                new AzureADUser { ObjectId = Guid.NewGuid(), Mail = "owner1@domain.com", UserPrincipalName = "owner1@domain.com" },
                new AzureADUser { ObjectId = Guid.NewGuid(), Mail = "owner2@domain.com", UserPrincipalName = "owner2@domain.com" }
            };

            _graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(groupId, It.IsAny<int>())).ReturnsAsync(expectedGroupOwners);

            var response = await _destinationController.GetGroupOwnersAsync(groupId);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(200, result?.StatusCode);
            Assert.IsNotNull(result.Value);
        }

        [TestMethod]
        public async Task GetGroupOwnersThrowsExceptionAsync()
        {
            _destinationController = new DestinationController(_searchGroupsHandler, _searchChannelsHandler, _getGroupEndpointsHandler, _getGroupOwnersHandler, _getGroupOnboardingStatusHandler, _getChannelOnboardingStatusHandler, _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", Guid.NewGuid().ToString())
                })
            };

            var groupId = Guid.NewGuid();
            _graphGroupRepository.Setup(x => x.GetGroupOwnersAsync(groupId, It.IsAny<int>())).ThrowsAsync(new Exception("Graph error"));

            var response = await _destinationController.GetGroupOwnersAsync(groupId);
            var result = response.Result as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(500, result?.StatusCode);
        }

        [TestMethod]
        public async Task GetGroupMembersSuccessAsync()
        {
            var groupId = Guid.NewGuid();
            var expectedGroups = new List<AzureADGroup>
            {
                new AzureADGroup { ObjectId = Guid.NewGuid(), Name = "Group1" },
                new AzureADGroup { ObjectId = Guid.NewGuid(), Name = "Group2" },
                new AzureADGroup { ObjectId = Guid.NewGuid(), Name = "Group3" }
            };

            _graphGroupRepository.Setup(x => x.GetDirectGroupTypeMembersAsync(groupId)).ReturnsAsync(expectedGroups);

            var mockHandler = new Mock<Services.Contracts.IRequestHandler<Services.Messages.Requests.GetGroupMembersRequest, Services.Messages.Responses.GetGroupMembersResponse>>();
            var handler = new GetGroupMembersHandler(_loggingRepository.Object, _graphGroupRepository.Object);

            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider.Setup(x => x.GetService(typeof(Services.Contracts.IRequestHandler<Services.Messages.Requests.GetGroupMembersRequest, Services.Messages.Responses.GetGroupMembersResponse>)))
                .Returns(handler);

            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = mockServiceProvider.Object;

            _destinationController = new DestinationController(_searchGroupsHandler, _searchChannelsHandler, _getGroupEndpointsHandler, _getGroupOwnersHandler, _getGroupOnboardingStatusHandler, _getChannelOnboardingStatusHandler, _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(httpContext)
            };

            var response = await _destinationController.GetGroupMembersAsync(groupId);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(200, result?.StatusCode);
            Assert.IsNotNull(result.Value);

            var groupMembersResponse = result.Value as GetGroupMembersResponse;
            Assert.IsNotNull(groupMembersResponse);
            Assert.AreEqual(groupId, groupMembersResponse.GroupId);
            Assert.AreEqual(expectedGroups.Count, groupMembersResponse.GroupMemberCount);
            Assert.AreEqual(expectedGroups.Count, groupMembersResponse.Groups.Count);
        }

        [TestMethod]
        public async Task GetGroupMembersWithEmptyGuidReturnsBadRequestAsync()
        {
            var handler = new GetGroupMembersHandler(_loggingRepository.Object, _graphGroupRepository.Object);

            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider.Setup(x => x.GetService(typeof(Services.Contracts.IRequestHandler<Services.Messages.Requests.GetGroupMembersRequest, Services.Messages.Responses.GetGroupMembersResponse>)))
                .Returns(handler);

            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = mockServiceProvider.Object;

            _destinationController = new DestinationController(_searchGroupsHandler, _searchChannelsHandler, _getGroupEndpointsHandler, _getGroupOwnersHandler, _getGroupOnboardingStatusHandler, _getChannelOnboardingStatusHandler, _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(httpContext)
            };

            var response = await _destinationController.GetGroupMembersAsync(Guid.Empty);
            var result = response.Result as BadRequestObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(400, result?.StatusCode);
        }

        [TestMethod]
        public async Task GetGroupMembersUnauthorizedReturnsForbidAsync()
        {
            var groupId = Guid.NewGuid();

            var mockHandler = new Mock<Services.Contracts.IRequestHandler<Services.Messages.Requests.GetGroupMembersRequest, Services.Messages.Responses.GetGroupMembersResponse>>();
            mockHandler.Setup(x => x.ExecuteAsync(It.IsAny<Services.Messages.Requests.GetGroupMembersRequest>()))
                .ThrowsAsync(new UnauthorizedAccessException());

            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider.Setup(x => x.GetService(typeof(Services.Contracts.IRequestHandler<Services.Messages.Requests.GetGroupMembersRequest, Services.Messages.Responses.GetGroupMembersResponse>)))
                .Returns(mockHandler.Object);

            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = mockServiceProvider.Object;

            _destinationController = new DestinationController(_searchGroupsHandler, _searchChannelsHandler, _getGroupEndpointsHandler, _getGroupOwnersHandler, _getGroupOnboardingStatusHandler, _getChannelOnboardingStatusHandler, _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(httpContext)
            };

            var response = await _destinationController.GetGroupMembersAsync(groupId);
            var result = response.Result as ForbidResult;

            Assert.IsNotNull(result);
        }

        [TestMethod]
        public async Task GetGroupMembersThrowsExceptionAsync()
        {
            var groupId = Guid.NewGuid();

            var mockHandler = new Mock<Services.Contracts.IRequestHandler<Services.Messages.Requests.GetGroupMembersRequest, Services.Messages.Responses.GetGroupMembersResponse>>();
            mockHandler.Setup(x => x.ExecuteAsync(It.IsAny<Services.Messages.Requests.GetGroupMembersRequest>()))
                .ThrowsAsync(new Exception("Graph error"));

            var mockServiceProvider = new Mock<IServiceProvider>();
            mockServiceProvider.Setup(x => x.GetService(typeof(Services.Contracts.IRequestHandler<Services.Messages.Requests.GetGroupMembersRequest, Services.Messages.Responses.GetGroupMembersResponse>)))
                .Returns(mockHandler.Object);

            var httpContext = new DefaultHttpContext();
            httpContext.RequestServices = mockServiceProvider.Object;

            _destinationController = new DestinationController(_searchGroupsHandler, _searchChannelsHandler, _getGroupEndpointsHandler, _getGroupOwnersHandler, _getGroupOnboardingStatusHandler, _getChannelOnboardingStatusHandler, _postGroupHandler)
            {
                ControllerContext = CreateControllerContext(httpContext)
            };

            var response = await _destinationController.GetGroupMembersAsync(groupId);
            var result = response.Result as ObjectResult;

            Assert.IsNotNull(result);
            Assert.AreEqual(500, result?.StatusCode);
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

