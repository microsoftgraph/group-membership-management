// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebApi.Controllers.v1.Roles
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
            return User.IsInRole(Models.Roles.TENANT_ADMINISTRATOR);
        }

        [Authorize]
        [HttpGet("isSubmissionReviewer")]
        public ActionResult<bool> GetIsSubmissionReviewer()
        {
            return User.IsInRole(Models.Roles.TENANT_SUBMISSION_REVIEWER);
        }

        [Authorize]
        [HttpGet("isTenantJobEditor")]
        public ActionResult<bool> GetIsTenantJobEditor()
        {
            return User.IsInRole(Models.Roles.TENANT_JOB_EDITOR);
        }
    }
}