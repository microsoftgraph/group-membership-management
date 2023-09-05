// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Messages.Contracts.Requests;

namespace Services.Messages.Requests
{
    public class GetSettingsRequest : RequestBase
    {
        public GetSettingsRequest(string key)
        {
            this.Key = key;
        }

        public string Key { get; }
    }
}