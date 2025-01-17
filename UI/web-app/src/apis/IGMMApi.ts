// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IJobsApi } from './jobs';
import { IRolesApi } from './roles';
import { ISettingsApi } from './settings';
import { ISqlMembershipSourcesApi } from './sqlMembershipSources';
import { IOperationsApi } from './operations';
import { IDestinationsApi } from './destinations/IDestinationsApi';

export interface IGMMApi {
  settings: ISettingsApi;
  jobs: IJobsApi;
  roles: IRolesApi;
  destinations: IDestinationsApi;
  sqlMembershipSources: ISqlMembershipSourcesApi;
  operationsApi: IOperationsApi;
}
