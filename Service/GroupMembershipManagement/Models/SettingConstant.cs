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
            { SettingKey.PrivacyPolicyUrl, Guid.Parse("6328107C-7332-47D1-A29C-CF9A49109AB0")},
            { SettingKey.UIUrl, Guid.Parse("446FDA16-C27B-4E0C-BF4D-5E563F47FC61")},
            { SettingKey.CanReviewOwnSubmissions, Guid.Parse("F901FC06-E92E-4361-B4CF-7F7283FB312B")},
            { SettingKey.CreateGroupFeatureEnabled, Guid.Parse("A4B0C3E1-7F8D-4E9F-9A2C-5B6A0B8D7F1B") },
            { SettingKey.IsBusinessJustificationRequired, Guid.Parse("CDE677B2-F55C-4AD8-AB09-DE59F87AA5EE") },
            { SettingKey.IsDisclaimerEnabled, Guid.Parse("99D83E89-9507-4DC5-AC22-C8962B936B67") },
        };
    }
}
