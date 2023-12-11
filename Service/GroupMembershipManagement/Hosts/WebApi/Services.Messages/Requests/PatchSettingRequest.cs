// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Models;
using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class PatchSettingRequest : RequestBase
    {
        public PatchSettingRequest(SettingKey settingKey, string value)
        {
            this.SettingKey = settingKey;
            this.Value = value;
        }

        public SettingKey SettingKey { get; }
        public string Value { get; }
    }
}