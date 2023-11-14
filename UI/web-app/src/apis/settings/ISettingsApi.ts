// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Setting } from '../../models';

export interface ISettingsApi {
  fetchSettingByKey(settingKey: string): Promise<Setting>;
  updateSetting(setting: Setting): Promise<Setting>;
}
