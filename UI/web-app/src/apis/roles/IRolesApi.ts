// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.


export interface IRolesApi {
  getIsAdmin(): Promise<boolean>;
  getIsSubmissionReviewer(): Promise<boolean>;
  getIsTenantJobEditor(): Promise<boolean>;
}
