// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WebApi.Controllers.v1.Roles;
using WebApi.Models;
using WebApi.Models.DTOs;

namespace Services.Tests
{
    [TestClass]
    public class RolesControllerTests
    {

        private RolesController _rolesController = null!;

        [TestInitialize]
        public void Initialize()
        {
            _rolesController = new RolesController();
        }

        [TestMethod]
        public void GetAllRolesStatus_ForVariousUsers()
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Role, Roles.JOB_OWNER_WRITER),
                new Claim(ClaimTypes.Role, Roles.JOB_OWNER_READER),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_READER),
                new Claim(ClaimTypes.Role, Roles.JOB_TENANT_WRITER),
                new Claim(ClaimTypes.Role, Roles.SUBMISSION_REVIEWER),
                new Claim(ClaimTypes.Role, Roles.HYPERLINK_ADMINISTRATOR),
                new Claim(ClaimTypes.Role, Roles.CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR),
            };

            _rolesController.ControllerContext = CreateControllerContext(claims);

            var result = _rolesController.GetAllRoles();

            Assert.IsNotNull(result);
            var okResult = result.Result as OkObjectResult;
            Assert.IsNotNull(okResult);
            var rolesStatuses = okResult.Value as RolesObject;
            Assert.IsNotNull(rolesStatuses);
            Assert.IsTrue(rolesStatuses.IsJobOwnerWriter);
            Assert.IsTrue(rolesStatuses.IsJobOwnerReader);
            Assert.IsTrue(rolesStatuses.IsJobTenantReader);
            Assert.IsTrue(rolesStatuses.IsJobTenantWriter);
            Assert.IsTrue(rolesStatuses.IsSubmissionReviewer);
            Assert.IsTrue(rolesStatuses.IsHyperlinkAdministrator);
            Assert.IsTrue(rolesStatuses.IsCustomMembershipProviderAdministrator);
        }

        private ControllerContext CreateControllerContext(List<Claim> claims)
        {
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var principal = new ClaimsPrincipal(identity);
            var httpContext = new DefaultHttpContext();
            httpContext.User = principal;

            return new ControllerContext { HttpContext = httpContext };
        }
    }
}