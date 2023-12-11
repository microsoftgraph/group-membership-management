// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;

namespace Models
{

    public class Setting
    {
        public Setting()
        {
        }

        public Setting(Guid id, SettingKey settingKey, string settingValue)
        {
            Id = id;
            SettingKey = settingKey;
            SettingValue = settingValue;
        }

        public Guid Id { get; set; }
        public SettingKey SettingKey { get; set; }
        public string SettingValue { get; set; }
    }
}