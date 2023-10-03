// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Moq;
using Repositories.Contracts;
using WebApi.Controllers.v1.Group;
using Microsoft.AspNetCore.Http;
using WebApi.Models.Responses;
using Models;
using Tavis.UriTemplates;

namespace Services.Tests
{
    [TestClass]
    public class GroupInformationControllerTests
    {
        private int _groupCount = 100;
        private Guid _validGroupId = Guid.NewGuid();
        private List<string> _groupTypes = null!;
        private List<AzureADGroup> _groups = null!;
        private HttpContext _context = null!;
        private GroupInformationController _groupInformationController = null!;
        private Mock<ILoggingRepository> _loggingRepository = null!;
        private Mock<IGraphGroupRepository> _graphGroupRepository = null!;
        private SearchGroupsHandler _searchGroupsHandler = null!;

        [TestInitialize]
        public void Initialize()
        {
            _context = new DefaultHttpContext();
            _groups = new List<AzureADGroup>();
            _loggingRepository = new Mock<ILoggingRepository>();
            _graphGroupRepository = new Mock<IGraphGroupRepository>();
            _searchGroupsHandler = new SearchGroupsHandler(_loggingRepository.Object, _graphGroupRepository.Object);
            _groupInformationController = new GroupInformationController(_searchGroupsHandler, _graphGroupRepository.Object)
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

            foreach (var index in Enumerable.Range(0, _groupCount))
            {
                var group = new AzureADGroup
                {
                    ObjectId = Guid.NewGuid(),
                    Type = _groupTypes[Random.Shared.Next(0, _groupTypes.Count)]
                };

                var groupName = $"Test Group {index}";
                _groups.Add(group);
            }

            _graphGroupRepository.Setup(x => x.SearchGroupsAsync(It.IsAny<string>())).ReturnsAsync(() => _groups);
            _graphGroupRepository.Setup(x => x.IsAppIDOwnerOfGroup(It.IsAny<string>(), It.Is<Guid>(g => g == _validGroupId))).ReturnsAsync(true);
        }

        [TestMethod]
        public async Task SearchGroupsTestAsync()
        {
            var response = await _groupInformationController.SearchAsync("Test");
            var result = response.Result as OkObjectResult;

            Assert.IsNotNull(response);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Value);

            var groups = result.Value as GetGroupsModel;

            Assert.IsNotNull(groups);
            Assert.AreEqual(_groupCount, groups.Count);

        }

        [TestMethod]
        public async Task IsAppIDOwnerOfGroupTestAsync()
        {
            var response = await _groupInformationController.IsAppIDOwnerOfGroupAsync(_validGroupId);
            Assert.IsNotNull(response);
            Assert.IsTrue(response.Value);
        }

    }
}

