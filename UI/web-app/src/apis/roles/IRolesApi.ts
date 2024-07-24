// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface Roles {
  isJobOwnerReader(): boolean;
  isJobOwnerEnabler(): boolean;
  isJobOwnerDeleter(): boolean;
  isJobOwnerConfigurationEditor(): boolean;
  isJobOwnerWriter(): boolean;
  isJobTenantReader(): boolean;
  isJobTenantWriter(): boolean;
  isHyperlinkAdministrator(): boolean;
  isCustomMembershipProviderAdministrator(): boolean;
  isOperationsResetAdministrator(): boolean;
}


export interface IRolesApi {
  getAllRoles(): Promise<Roles>;
}