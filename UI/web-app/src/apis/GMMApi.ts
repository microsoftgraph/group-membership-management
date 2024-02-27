// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ApiOptions } from './ApiOptions';
import { IGMMApi } from './IGMMApi';
import { IJobsApi, JobsApi } from './jobs';
import { IRolesApi, RolesApi } from './roles';
import { ISettingsApi } from './settings/ISettingsApi';
import { SettingsApi } from './settings/SettingsApi';
import { ISqlMembershipSourcesApi, SqlMembershipSourcesApi } from './sqlMembershipSources';

export class GMMApi implements IGMMApi {
  private _jobsApi: IJobsApi;
  private _settingsApi: ISettingsApi;
  private _rolesApi: IRolesApi;
  private _sqlMembershipSourcesApi: ISqlMembershipSourcesApi;
  
  constructor(options: ApiOptions) {
    const { baseUrl } = options;
    this._jobsApi = new JobsApi({ ...options, baseUrl: `${baseUrl}/jobs` });
    this._settingsApi = new SettingsApi({ ...options, baseUrl: `${baseUrl}/settings` });
    this._rolesApi = new RolesApi({ ...options, baseUrl: `${baseUrl}/roles` });
    this._sqlMembershipSourcesApi = new SqlMembershipSourcesApi({ ...options, baseUrl: `${baseUrl}/sqlMembershipSources` });

  }

  public get jobs(): IJobsApi {
    return this._jobsApi;
  }
  public get settings(): ISettingsApi {
    return this._settingsApi;
  }
  public get roles(): IRolesApi {
    return this._rolesApi;
  }
  public get sqlMembershipSources(): ISqlMembershipSourcesApi {
    return this._sqlMembershipSourcesApi;
  }
}
