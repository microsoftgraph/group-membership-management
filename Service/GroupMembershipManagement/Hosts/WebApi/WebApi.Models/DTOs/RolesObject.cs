// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class RolesObject
    {
        public RolesObject()
        {

        }
        public bool IsJobOwnerReader { get; set; }
        public bool IsJobOwnerEnabler { get; set; }
        public bool IsJobOwnerDeleter { get; set; }
        public bool IsJobOwnerConfigurationEditor { get; set; }
        public bool IsJobOwnerWriter { get; set; }
        public bool IsJobTenantReader { get; set; }
        public bool IsJobTenantWriter { get; set; }
        public bool IsSubmissionReviewer { get; set; }
        /// <summary>
        /// Deprecated. The Hyperlink.ReadWrite.All app role has been retired and hyperlink
        /// administration is covered by GeneralSettings.ReadWrite.All. This property is retained
        /// as an always-false v1 response compatibility field and grants no access.
        /// </summary>
        [Obsolete("Hyperlink.ReadWrite.All has been retired. Use IsGeneralSettingsAdministrator.")]
        public bool IsHyperlinkAdministrator { get; set; }

        public bool IsCustomMembershipProviderAdministrator { get; set; }
        public bool IsOperationsResetAdministrator { get; set; }
        public bool IsGeneralSettingsAdministrator { get; set; }
        public bool IsAutoApproverAdministrator { get; set; }
        public bool IsAIOnboardingChat { get; set; }
        public bool IsAISettingsAdministrator { get; set; }
        public bool IsAISyncJob { get; set; }
        public bool IsTeamsChannelOnboarder { get; set; }

        /// <summary>
        /// View-only signals. Each is true when the caller holds the area's Read role or its
        /// ReadWrite role, because read-write implies read. The matching administrator property
        /// remains the sole "can change" signal and is never widened by these.
        /// </summary>
        public bool IsGeneralSettingsReader { get; set; }
        public bool IsAutoApproverReader { get; set; }
        public bool IsAISettingsReader { get; set; }
        public bool IsCustomMembershipProviderReader { get; set; }
    }
}
