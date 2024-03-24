// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface Roles {
  isJobCreator(): boolean;
  isJobTenantReader(): boolean;
  isJobTenantWriter(): boolean;
  isHyperlinkAdministrator(): boolean;
  isCustomMembershipProviderAdministrator(): boolean;
}


export interface IRolesApi {
  getAllRoles(): Promise<Roles>;
}