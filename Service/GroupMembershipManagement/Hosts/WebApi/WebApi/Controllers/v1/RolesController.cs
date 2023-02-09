// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers.v1
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/roles")]
    public class RolesController : ControllerBase
    {

        public RolesController()
        {
        }

        [Authorize]
        [HttpGet("isAdmin")]
        public ActionResult<bool> GetIsAdmin()
        {
            return User.IsInRole("Admin");
        }
    }
}