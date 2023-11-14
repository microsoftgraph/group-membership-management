// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IJobsApi } from './jobs';
import { ISettingsApi } from './settings';

export interface IGMMApi {
  settings: ISettingsApi;
  jobs: IJobsApi;
}
