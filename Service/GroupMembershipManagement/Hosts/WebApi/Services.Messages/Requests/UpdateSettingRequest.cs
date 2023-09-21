// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class UpdateSettingRequest : RequestBase
    {
        public UpdateSettingRequest(string key, string value)
        {
            this.Key = key;
            this.Value = value;
        }

        public string Key { get; }
        public string Value { get; }
    }
}