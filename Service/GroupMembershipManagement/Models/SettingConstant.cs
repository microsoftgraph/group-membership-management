// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;

namespace Models
{
    public class SettingConstants
    {
        public static readonly Dictionary<SettingKey, Guid> SettingIds = new Dictionary<SettingKey, Guid>
        {
            { SettingKey.DashboardUrl, Guid.Parse("63BA3339-639A-4104-AC63-E1376F0445C9") },
            { SettingKey.OutlookWarningUrl, Guid.Parse("DFF1D616-E1E7-4642-B37F-FDE617158A90")},
            { SettingKey.PrivacyPolicyUrl, Guid.Parse("6328107C-7332-47D1-A29C-CF9A49109AB0")}
        };
    }
}
