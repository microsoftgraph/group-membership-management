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
        [HttpGet("getAllRoles")]
        public ActionResult<Models.DTOs.RolesObject> GetAllRoles()
        {
            var roleStatus = new Models.DTOs.RolesObject
            {
                IsJobCreator = User.IsInRole(Models.Roles.JOB_OWNER_WRITER),
                IsJobTenantReader = User.IsInRole(Models.Roles.JOB_TENANT_READER),
                IsJobTenantWriter = User.IsInRole(Models.Roles.JOB_TENANT_WRITER),
                IsSubmissionReviewer = User.IsInRole(Models.Roles.SUBMISSION_REVIEWER),
                IsHyperlinkAdministrator = User.IsInRole(Models.Roles.HYPERLINK_ADMINISTRATOR),
                IsCustomMembershipProviderAdministrator = User.IsInRole(Models.Roles.CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR)
            };

            return Ok(roleStatus);
        }
    }
}