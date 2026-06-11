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
            var isJobOwnerReader = User.IsInRole(Models.Roles.JOB_OWNER_READER);
            var isJobOwnerEnabler = User.IsInRole(Models.Roles.JOB_OWNER_ENABLER);
            var isJobOwnerDeleter = User.IsInRole(Models.Roles.JOB_OWNER_DELETER);
            var isJobOwnerConfigurationEditor = User.IsInRole(Models.Roles.JOB_OWNER_CONFIGURATION_EDITOR);
            var isJobOwnerWriter = User.IsInRole(Models.Roles.JOB_OWNER_WRITER);

            if (User.IsInRole(Models.Roles.JOB_OWNER_WRITER))
            {
                isJobOwnerReader = true;
                isJobOwnerEnabler = true;
                isJobOwnerDeleter = true;
                isJobOwnerConfigurationEditor = true;
            };

            var isJobTenantReader = User.IsInRole(Models.Roles.JOB_TENANT_READER);
            var isJobTenantWriter = User.IsInRole(Models.Roles.JOB_TENANT_WRITER);
            var isSubmissionReviewer = User.IsInRole(Models.Roles.SUBMISSION_REVIEWER);
            var isSubmissionRejector = User.IsInRole(Models.Roles.SUBMISSION_REJECTOR);
            var isHyperlinkAdministrator = User.IsInRole(Models.Roles.HYPERLINK_ADMINISTRATOR);
            var isCustomMembershipProviderAdministrator = User.IsInRole(Models.Roles.CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR);
            var isOperationsResetAdministrator = User.IsInRole(Models.Roles.RESET_ADMINISTRATOR);
            var isGeneralSettingsAdministrator = User.IsInRole(Models.Roles.GENERAL_SETTINGS_ADMINISTRATOR);

            var roleStatus = new Models.DTOs.RolesObject
            {
                IsJobOwnerReader = isJobOwnerReader,
                IsJobOwnerEnabler = isJobOwnerEnabler,
                IsJobOwnerConfigurationEditor = isJobOwnerConfigurationEditor,
                IsJobOwnerDeleter = isJobOwnerDeleter,
                IsJobOwnerWriter = isJobOwnerWriter,
                IsJobTenantReader = isJobTenantReader,
                IsJobTenantWriter = isJobTenantWriter,
                IsSubmissionReviewer = isSubmissionReviewer,
                IsSubmissionRejector = isSubmissionRejector,
                IsHyperlinkAdministrator = isHyperlinkAdministrator,
                IsCustomMembershipProviderAdministrator = isCustomMembershipProviderAdministrator,
                IsOperationsResetAdministrator = isOperationsResetAdministrator,
                IsGeneralSettingsAdministrator = isGeneralSettingsAdministrator
            };

            return Ok(roleStatus);
        }
    }
}