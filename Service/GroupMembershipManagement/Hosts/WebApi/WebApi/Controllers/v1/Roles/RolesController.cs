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
            var isHyperlinkAdministrator = false;
            var isCustomMembershipProviderAdministrator = User.IsInRole(Models.Roles.CUSTOM_MEMBERSHIP_PROVIDER_ADMINISTRATOR);
            var isOperationsResetAdministrator = User.IsInRole(Models.Roles.RESET_ADMINISTRATOR);
            var isGeneralSettingsAdministrator = User.IsInRole(Models.Roles.GENERAL_SETTINGS_ADMINISTRATOR);
            var isAutoApproverAdministrator = User.IsInRole(Models.Roles.AUTO_APPROVER_ADMINISTRATOR);
            var isAIOnboardingChat = User.IsInRole(Models.Roles.AI_ONBOARDING_CHAT);
            var isAISettingsAdministrator = User.IsInRole(Models.Roles.AI_SETTINGS_ADMINISTRATOR);
            var isAISyncJob = User.IsInRole(Models.Roles.AI_SYNC_JOB);
            var isTeamsChannelOnboarder = User.IsInRole(Models.Roles.TEAMS_CHANNEL_ONBOARDER);

            // Read-write implies read, so an existing administrator reports as a reader without
            // being granted the new role. The administrator flags above stay ReadWrite-only.
            var isGeneralSettingsReader = User.IsInRole(Models.Roles.GENERAL_SETTINGS_READER) || isGeneralSettingsAdministrator;
            var isAutoApproverReader = User.IsInRole(Models.Roles.AUTO_APPROVER_READER) || isAutoApproverAdministrator;
            var isAISettingsReader = User.IsInRole(Models.Roles.AI_SETTINGS_READER) || isAISettingsAdministrator;
            var isCustomMembershipProviderReader = User.IsInRole(Models.Roles.CUSTOM_MEMBERSHIP_PROVIDER_READER) || isCustomMembershipProviderAdministrator;

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
#pragma warning disable CS0618 // Deprecated always-false v1 compatibility field.
                IsHyperlinkAdministrator = isHyperlinkAdministrator,
#pragma warning restore CS0618
                IsCustomMembershipProviderAdministrator = isCustomMembershipProviderAdministrator,
                IsOperationsResetAdministrator = isOperationsResetAdministrator,
                IsGeneralSettingsAdministrator = isGeneralSettingsAdministrator,
                IsAutoApproverAdministrator = isAutoApproverAdministrator,
                IsAIOnboardingChat = isAIOnboardingChat,
                IsAISettingsAdministrator = isAISettingsAdministrator,
                IsAISyncJob = isAISyncJob,
                IsTeamsChannelOnboarder = isTeamsChannelOnboarder,
                IsGeneralSettingsReader = isGeneralSettingsReader,
                IsAutoApproverReader = isAutoApproverReader,
                IsAISettingsReader = isAISettingsReader,
                IsCustomMembershipProviderReader = isCustomMembershipProviderReader
            };

            return Ok(roleStatus);
        }
    }
}