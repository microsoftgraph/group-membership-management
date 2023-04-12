// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export type Account = {
  homeAccountId: string;
  environment: string;
  tenantId: string;
  username: string;
  localAccountId: string;
  name?: string;
  idToken?: string;
  nativeAccountId?: string;
};

