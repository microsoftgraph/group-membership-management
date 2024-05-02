// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Repositories.Contracts;
using Services.Messages.Responses;
using System.Security.Claims;
using WebApi.Controllers.v1.OrgLeaderDetails;
using WebApi.Models;

namespace Services.Tests
{
    [TestClass]
    public class OrgLeaderDetailsControllerTests
    {
        private Mock<ILoggingRepository> _loggingRepository = null;
        private Mock<IDataFactoryRepository> _dataFactoryRepository = null!;
        private Mock<ISqlMembershipRepository> _sqlMembershipRepository = null!;

        private GetOrgLeaderDetailsHandler _getOrgLeaderDetailsHandler = null!;
        private GetOrgLeaderHandler _getOrgLeaderHandler = null!;
        private OrgLeaderDetailsController _orgLeaderDetailsController = null!;

        [TestInitialize]
        public void Initialize()
        {
            _loggingRepository = new Mock<ILoggingRepository>();
            _dataFactoryRepository = new Mock<IDataFactoryRepository>();
            _sqlMembershipRepository = new Mock<ISqlMembershipRepository>();

            _getOrgLeaderDetailsHandler = new GetOrgLeaderDetailsHandler(_loggingRepository.Object, _sqlMembershipRepository.Object, _dataFactoryRepository.Object);
            _getOrgLeaderHandler = new GetOrgLeaderHandler(_loggingRepository.Object, _sqlMembershipRepository.Object, _dataFactoryRepository.Object);
            _orgLeaderDetailsController = new OrgLeaderDetailsController(_getOrgLeaderDetailsHandler, _getOrgLeaderHandler)
            {
                ControllerContext = CreateControllerContext(new List<Claim>
                {
                    new Claim(ClaimTypes.Name, "user@domain.com"),
                    new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER)
                })
            };

            _sqlMembershipRepository.Setup(x => x.CheckIfTableExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
            _sqlMembershipRepository.Setup(x => x.GetOrgLeaderDetailsAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync((5, 123));
            _dataFactoryRepository.Setup(x => x.GetMostRecentSucceededRunIdAsync()).ReturnsAsync("RUN ID");
        }

        [TestMethod]
        public async Task GetOrgLeaderDetailsAsyncTestAsync()
        {
            var response = await _orgLeaderDetailsController.GetOrgLeaderDetailsAsync("");
            Assert.IsNotNull(response);
            var okResult = response.Result as OkObjectResult;
            Assert.IsNotNull(okResult);
            Assert.IsNotNull(okResult.Value);
            var orgLeaderDetails = okResult.Value as GetOrgLeaderDetailsResponse;
            Assert.IsNotNull(orgLeaderDetails);
            Assert.AreEqual(orgLeaderDetails.MaxDepth, 5);
            Assert.AreEqual(orgLeaderDetails.EmployeeId, 123);
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