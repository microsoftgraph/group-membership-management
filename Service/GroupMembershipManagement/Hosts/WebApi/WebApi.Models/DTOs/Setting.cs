// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;

namespace WebApi.Models.DTOs
{
    public class Setting
    {
        public Setting(SettingKey settingKey, string settingValue)
        {
            SettingKey = settingKey;
            SettingValue = settingValue;
        }
        public SettingKey SettingKey { get; set; }
        public string SettingValue { get; set; }
    }
}
