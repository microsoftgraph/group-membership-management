// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IJobsApi } from './jobs';
import { IRolesApi } from './roles';
import { ISettingsApi } from './settings';

export interface IGMMApi {
  settings: ISettingsApi;
  jobs: IJobsApi;
  roles: IRolesApi;
}
