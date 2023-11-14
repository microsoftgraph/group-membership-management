// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { AxiosResponse } from 'axios';
import { Setting } from '../../models/Setting';
import { ApiBase } from '../ApiBase';
import { ISettingsApi } from './ISettingsApi';

export class SettingsApi extends ApiBase implements ISettingsApi {
  public async fetchSettingByKey(settingKey: string): Promise<Setting> {
    const response = await this.httpClient.get<Setting>('/', {
      params: {
        key: settingKey,
      },
    });
    this.ensureSuccessStatusCode(response);
    return response.data;
  }

  async updateSetting(setting: Setting): Promise<Setting> {
    const response = await this.httpClient.post<Setting, AxiosResponse<Setting>, string>(
      `/${encodeURIComponent(setting.key)}`,
      setting.value,
      { headers: { 'Content-Type': 'application/json' } }
    );
    this.ensureSuccessStatusCode(response);
    return response.data;
  }
}
