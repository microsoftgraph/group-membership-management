// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IJobsApi } from './jobs';
import { IRolesApi } from './roles';
import { ISettingsApi } from './settings';
import { ISqlMembershipSourcesApi } from './sqlMembershipSources';
import { IOperationsApi } from './operations';
import { IDestinationsApi } from './destinations/IDestinationsApi';
import { ITitleApi } from './titles';

export interface IGMMApi {
  title: ITitleApi;
  settings: ISettingsApi;
  jobs: IJobsApi;
  roles: IRolesApi;
  destinations: IDestinationsApi;
  sqlMembershipSources: ISqlMembershipSourcesApi;
  operationsApi: IOperationsApi;
}
