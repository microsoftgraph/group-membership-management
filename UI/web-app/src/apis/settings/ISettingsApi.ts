// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Setting } from '../../models';

export interface ISettingsApi {
  fetchSettings(): Promise<Setting[]>;
  fetchSettingByKey(settingKey: string): Promise<Setting>;
  patchSetting(setting: Setting): Promise<Setting>;
}
