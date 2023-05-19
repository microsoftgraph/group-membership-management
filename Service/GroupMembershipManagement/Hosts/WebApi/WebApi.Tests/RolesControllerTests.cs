// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using WebApi.Controllers.v1.Roles;

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
        public void Get_IsAdmin_ForAdminUser()
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Role, "Admin"),
            };

            _rolesController.ControllerContext = CreateControllerContext(claims);

            var result = _rolesController.GetIsAdmin();

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Value);
        }

        [TestMethod]
        public void Get_IsAdmin_ForRegularUser()
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Role, "Reader"),
            };

            _rolesController.ControllerContext = CreateControllerContext(claims);

            var result = _rolesController.GetIsAdmin();

            Assert.IsNotNull(result);
            Assert.IsFalse(result.Value);
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