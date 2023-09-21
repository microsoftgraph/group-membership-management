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
        public Setting(string key)
        {
            Key = key;
        }
        public string Key { get; set; }

        public string Value { get; set; }
    }
}