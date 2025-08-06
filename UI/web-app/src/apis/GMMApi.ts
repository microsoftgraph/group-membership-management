// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ApiOptions } from './ApiOptions';
import { IGMMApi } from './IGMMApi';
import { ITitleApi, TitleApi } from './titles';
import { IJobsApi, JobsApi } from './jobs';
import { IRolesApi, RolesApi } from './roles';
import { ISettingsApi } from './settings/ISettingsApi';
import { SettingsApi } from './settings/SettingsApi';
import { ISqlMembershipSourcesApi, SqlMembershipSourcesApi } from './sqlMembershipSources';
import { IOperationsApi, OperationsApi} from './operations';
import { IDestinationsApi } from './destinations/IDestinationsApi';
import { DestinationsApi } from './destinations/DestinationsApi';

export class GMMApi implements IGMMApi {
  private _titleApi: ITitleApi;
  private _jobsApi: IJobsApi;
  private _settingsApi: ISettingsApi;
  private _rolesApi: IRolesApi;
  private _sqlMembershipSourcesApi: ISqlMembershipSourcesApi;
  private _operationsApi: IOperationsApi;
  private _destinationsApi: IDestinationsApi;

  constructor(options: ApiOptions) {
    const { baseUrl } = options;
    this._titleApi = new TitleApi({ ...options, baseUrl: `${baseUrl}` });
    this._jobsApi = new JobsApi({ ...options, baseUrl: `${baseUrl}/jobs` });
    this._settingsApi = new SettingsApi({ ...options, baseUrl: `${baseUrl}/settings` });
    this._rolesApi = new RolesApi({ ...options, baseUrl: `${baseUrl}/roles` });
    this._sqlMembershipSourcesApi = new SqlMembershipSourcesApi({ ...options, baseUrl: `${baseUrl}/sqlMembershipSources` });
    this._operationsApi = new OperationsApi({ ...options, baseUrl: `${baseUrl}/operations` });
    this._destinationsApi = new DestinationsApi({ ...options, baseUrl: `${baseUrl}/destinations` });
  }

  public get title(): ITitleApi {
    return this._titleApi;
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
  public get operationsApi(): IOperationsApi {
    return this._operationsApi;
  }
  public get destinations(): IDestinationsApi {
    return this._destinationsApi;
  }
}
