// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { SettingKey } from './SettingKey';
import { SettingName } from './SettingName';

export type Setting = {
    settingKey: SettingKey;
    settingName: SettingName;
    settingValue: string;
}