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
using Microsoft.Graph.Models;
using Services.Messages.Responses;
using Tavis.UriTemplates;

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
        private HttpContext _context = null!;
        private DestinationController _destinationController = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private SearchDestinationsHandler _searchDestinationsHandler = null!;
        private GetGroupEndpointsHandler _getGroupEndpointsHandler = null!;
        private GetGroupOnboardingStatusHandler _getGroupOnboardingStatusHandler = null!;
        private Mock<IOptions<GraphCredentials>> _graphCredentials = null!;

        [TestInitialize]
        public void Initialize()
        {
            _context = new DefaultHttpContext();
            _destinations = new List<AzureADGroup>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _searchDestinationsHandler = new SearchDestinationsHandler(_loggingRepository.Object, _graphGroupRepository.Object);
            _getGroupEndpointsHandler = new GetGroupEndpointsHandler(_loggingRepository.Object, _graphGroupRepository.Object);
            _graphCredentials = new Mock<IOptions<GraphCredentials>>();
            var testGraphCredentials = new GraphCredentials
            {
                ClientId = "00000003-0000-0000-c000-000000000000"
            };

            _graphCredentials.Setup(gc => gc.Value).Returns(testGraphCredentials);
            _getGroupOnboardingStatusHandler = new GetGroupOnboardingStatusHandler(_loggingRepository.Object, 
                                                                                   _graphGroupRepository.Object, 
                                                                                   _graphCredentials.Object);

            _destinationController = new DestinationController(_searchDestinationsHandler, _getGroupEndpointsHandler, _getGroupOnboardingStatusHandler)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = _context
                }
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

            _graphGroupRepository.Setup(x => x.SearchDestinationsAsync(It.IsAny<string>())).ReturnsAsync(() => _destinations);
            _graphGroupRepository.Setup(x => x.IsAppIDOwnerOfGroup(It.IsAny<string>(), It.Is<Guid>(g => g == _validDestinationId))).ReturnsAsync(true);
            _graphGroupRepository.Setup(x => x.GetGroupEndpointsAsync(It.IsAny<Guid>())).ReturnsAsync(_expectedEndpoints);
        }

        [TestMethod]
        public async Task SearchDestinationsTestAsync()
        {
            var response = await _destinationController.SearchAsync("Test");
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var destinations = result.Value as GetDestinationsModel;
            Assert.IsNotNull(destinations);
            Assert.AreEqual(_destinationCount, destinations.Count);
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
        public async Task GetGroupOnboardingStatusAsync()
        {
            var response = await _destinationController.GetGroupOnboardingStatusAsync(_validDestinationId);
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result?.Value);

            var onboardingStatus = result.Value;
            Assert.IsNotNull(onboardingStatus);
            Assert.AreEqual(OnboardingStatus.ReadyForOnboarding, onboardingStatus);
        }
    }
}

