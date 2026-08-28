// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface Roles {
  isJobOwnerReader: boolean;
  isJobOwnerEnabler: boolean;
  isJobOwnerDeleter: boolean;
  isJobOwnerWriter: boolean;
  isJobTenantReader: boolean;
  isJobTenantWriter: boolean;
  isSubmissionReviewer: boolean;
  isAutoApproverAdministrator: boolean;
  isCustomMembershipProviderAdministrator: boolean;
  isOperationsResetAdministrator: boolean;
  isGeneralSettingsAdministrator: boolean;
  isAIOnboardingChat: boolean;
  isAISettingsAdministrator: boolean;
  isAISyncJob: boolean;
  isTeamsChannelOnboarder: boolean;
  isGeneralSettingsReader: boolean;
  isAutoApproverReader: boolean;
  isAISettingsReader: boolean;
  isCustomMembershipProviderReader: boolean;
  isFetchingRoles: boolean;
}


export interface IRolesApi {
  getAllRoles(): Promise<Roles>;
};
