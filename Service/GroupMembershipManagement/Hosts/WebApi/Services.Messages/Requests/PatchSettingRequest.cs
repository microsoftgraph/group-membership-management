// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class PatchSettingRequest : RequestBase
    {
        public string SettingValue { get; }
        public SettingKey SettingKey { get; }
        public PatchSettingRequest(SettingKey settingKey, string settingValue)
        {
            SettingKey = settingKey;
            SettingValue = settingValue;
        }
    }
}