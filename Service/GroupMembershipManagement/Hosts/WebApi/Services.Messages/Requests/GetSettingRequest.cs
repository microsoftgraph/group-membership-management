// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetSettingRequest : RequestBase
    {
        public GetSettingRequest(string key)
        {
            this.Key = key;
        }

        public string Key { get; }
    }
}