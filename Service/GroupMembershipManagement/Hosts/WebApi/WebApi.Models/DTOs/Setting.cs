// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

namespace WebApi.Models.DTOs
{
    public class Setting
    {
        public Setting(string key, string value)
        {
            Key = key;
            Value = value;
        }
        public string Key { get; set; }
        public string Value { get; set; }
    }
}
