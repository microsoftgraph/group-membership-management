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
  isSubmissionRejector: boolean;
  isHyperlinkAdministrator: boolean;
  isCustomMembershipProviderAdministrator: boolean;
  isOperationsResetAdministrator: boolean;
  isGeneralSettingsAdministrator: boolean;
  isFetchingRoles: boolean;
}


export interface IRolesApi {
  getAllRoles(): Promise<Roles>;
}