// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export const enum SettingKey {
    DashboardUrl = 0,
    OutlookWarningUrl = 1,
    PrivacyPolicyUrl = 2,
    UIUrl = 3,
    CanReviewOwnSubmissions = 4,
    CreateGroupFeatureEnabled = 5,
    IsBusinessJustificationRequired = 6,
    IsDisclaimerEnabled = 7,
    IsAutoApprovalForGroupBasedSyncsEnabled = 8,
    IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled = 9,
}

export const SettingKeyMap: Record<SettingKey, string> = {
    [SettingKey.DashboardUrl]: 'DashboardUrl',
    [SettingKey.OutlookWarningUrl]: 'OutlookWarningUrl',
    [SettingKey.PrivacyPolicyUrl]: 'PrivacyPolicyUrl',
    [SettingKey.UIUrl]: 'UIUrl',
    [SettingKey.CanReviewOwnSubmissions]: 'CanReviewOwnSubmissions',
    [SettingKey.CreateGroupFeatureEnabled]: 'CreateGroupFeatureEnabled',
    [SettingKey.IsBusinessJustificationRequired]: 'IsBusinessJustificationRequired',
    [SettingKey.IsDisclaimerEnabled]: 'IsDisclaimerEnabled',
    [SettingKey.IsAutoApprovalForGroupBasedSyncsEnabled]: 'IsAutoApprovalForGroupBasedSyncsEnabled',
    [SettingKey.IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled]: 'IsAutoApprovalForRequestorIsOrgLeaderSyncsEnabled',
};
