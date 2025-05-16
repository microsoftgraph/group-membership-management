// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { AdminConfigProps } from './AdminConfig.types';
import {
  selectDashboardUrl,
  selectIsSaving,
  selectOutlookWarningUrl,
  selectPrivacyPolicyUrl,
  selectUIUrl,
  selectCanReviewOwnSubmissions,
  selectCreateGroupFeatureEnabled,
  selectIsBusinessJustificationRequired,
  selectIsDisclaimerEnabled
} from '../../store/settings.slice';
import { patchSetting } from '../../store/settings.api';
import { AppDispatch } from '../../store';
import { AdminConfigView } from './AdminConfig.view';
import { useStrings } from '../../store/hooks';
import { SettingKey } from '../../models/SettingKey';
import { setPagingBarVisible } from '../../store/pagingBar.slice';
import { selectSource, selectAttributes, selectIsSourceSaving, selectAreAttributesSaving, setSource, setAttributes } from '../../store/sqlMembershipSources.slice';
import { SqlMembershipAttribute, SqlMembershipSource } from '../../models';
import { fetchAttributeValues, patchDefaultSqlMembershipSourceAttributes, patchDefaultSqlMembershipSourceCustomLabel } from '../../store/sqlMembershipSources.api';
import { 
  selectIsCustomMembershipProviderAdministrator, 
  selectIsHyperlinkAdministrator, 
  selectIsOperationsResetAdministrator,
  selectIsGeneralSettingsAdministrator,
  selectHasAdminCenterPermissions,
} from '../../store/roles.slice';
import { MessageBar, MessageBarType } from '@fluentui/react';

export const AdminConfigBase: React.FunctionComponent<AdminConfigProps> = (props: AdminConfigProps) => {
  // get the store's dispatch function
  const dispatch = useDispatch<AppDispatch>();
  useEffect(() => {
    dispatch(setPagingBarVisible(false));
  }, [dispatch]);

  // get the settings data from the store
  const dashboardUrl = useSelector(selectDashboardUrl);
  const outlookWarningUrl = useSelector(selectOutlookWarningUrl);
  const privacyPolicyUrl = useSelector(selectPrivacyPolicyUrl);
  const UIUrl = useSelector(selectUIUrl);
  const canReviewOwnSubmissions = useSelector(selectCanReviewOwnSubmissions);
  const createGroupFeatureEnabled = useSelector(selectCreateGroupFeatureEnabled);
  const isBusinessJustificationRequired = useSelector(selectIsBusinessJustificationRequired);
  const IsDisclaimerEnabled = useSelector(selectIsDisclaimerEnabled);
  const sqlMembershipSource = useSelector(selectSource);
  const sqlMembershipSourceAttributes = useSelector(selectAttributes);
  const isSourceSaving = useSelector(selectIsSourceSaving);
  const areAttributesSaving = useSelector(selectAreAttributesSaving);
  const areSettingsSaving = useSelector(selectIsSaving);
  const isHyperlinkAdmin = useSelector(selectIsHyperlinkAdministrator);
  const isCustomMembershipProviderAdmin = useSelector(selectIsCustomMembershipProviderAdministrator);
  const isOperationsResetAdministrator = useSelector(selectIsOperationsResetAdministrator);
  const isGeneralSettingsAdministrator = useSelector(selectIsGeneralSettingsAdministrator);
  const canViewSettings = useSelector(selectHasAdminCenterPermissions);

  const strings = useStrings().AdminConfig;

  const generateSettings = () => ({
    [SettingKey.DashboardUrl]: dashboardUrl ?? '',
    [SettingKey.OutlookWarningUrl]: outlookWarningUrl ?? '',
    [SettingKey.PrivacyPolicyUrl]: privacyPolicyUrl ?? '',
    [SettingKey.UIUrl]: UIUrl ?? '',
    [SettingKey.CanReviewOwnSubmissions]: canReviewOwnSubmissions ? 'true' : 'false',
    [SettingKey.CreateGroupFeatureEnabled]: createGroupFeatureEnabled ? 'true' : 'false',
    [SettingKey.IsBusinessJustificationRequired]: isBusinessJustificationRequired ? 'true' : 'false',
    [SettingKey.IsDisclaimerEnabled]: IsDisclaimerEnabled ? 'true' : 'false',
  });

  const [settings, setSettings] = useState<{ readonly [key in SettingKey]: string }>(generateSettings());

  useEffect(() => { 
    setSettings(generateSettings())
  }, [dashboardUrl, outlookWarningUrl, privacyPolicyUrl, canReviewOwnSubmissions, createGroupFeatureEnabled, isBusinessJustificationRequired, IsDisclaimerEnabled]);

  const handleGetValues = (attribute: SqlMembershipAttribute) => {
    dispatch(fetchAttributeValues(attribute));
  }

  // Update boolean values to strings before dispatching patchSetting
  const handleSave = (newSettings: { readonly [key in SettingKey]: string }, newSqlMembershipSource: SqlMembershipSource | undefined, newSqlMembershipAttributes: SqlMembershipAttribute[] | undefined) => {
    const formattedSettings = {
      ...newSettings,
      [SettingKey.CanReviewOwnSubmissions]: newSettings[SettingKey.CanReviewOwnSubmissions] === 'true' ? 'true' : 'false',
      [SettingKey.CreateGroupFeatureEnabled]: newSettings[SettingKey.CreateGroupFeatureEnabled] === 'true' ? 'true' : 'false',
      [SettingKey.IsBusinessJustificationRequired]: newSettings[SettingKey.IsBusinessJustificationRequired] === 'true' ? 'true' : 'false',
      [SettingKey.IsDisclaimerEnabled]: newSettings[SettingKey.IsDisclaimerEnabled] === 'true' ? 'true' : 'false',
    };
  
    if (JSON.stringify(formattedSettings) !== JSON.stringify(settings)) {
      setSettings(formattedSettings);
  
      dispatch(
        patchSetting({
          settingKey: SettingKey.DashboardUrl,
          settingValue: formattedSettings[SettingKey.DashboardUrl],
        })
      );
      dispatch(patchSetting({
        settingKey: SettingKey.OutlookWarningUrl,
        settingValue: formattedSettings[SettingKey.OutlookWarningUrl]
      }));
      dispatch(
        patchSetting({
          settingKey: SettingKey.PrivacyPolicyUrl,
          settingValue: formattedSettings[SettingKey.PrivacyPolicyUrl],
        })
      );
      dispatch(
        patchSetting({
          settingKey: SettingKey.CanReviewOwnSubmissions,
          settingValue: formattedSettings[SettingKey.CanReviewOwnSubmissions],
        })
      );
      dispatch(
        patchSetting({
          settingKey: SettingKey.CreateGroupFeatureEnabled,
          settingValue: formattedSettings[SettingKey.CreateGroupFeatureEnabled],
        })
      );
      dispatch(
        patchSetting({
          settingKey: SettingKey.IsBusinessJustificationRequired,
          settingValue: formattedSettings[SettingKey.IsBusinessJustificationRequired],
        })
      );
      dispatch(
        patchSetting({
          settingKey: SettingKey.IsDisclaimerEnabled,
          settingValue: formattedSettings[SettingKey.IsDisclaimerEnabled],
        })
      );
    }
  
    if (JSON.stringify(newSqlMembershipSource) !== JSON.stringify(sqlMembershipSource)) {
      dispatch(
        patchDefaultSqlMembershipSourceCustomLabel(newSqlMembershipSource?.customLabel ?? '')
      );
  
      dispatch(
        setSource(newSqlMembershipSource)
      );
    }
    
    if (JSON.stringify(newSqlMembershipAttributes) !== JSON.stringify(sqlMembershipSourceAttributes)) {
      dispatch(
        patchDefaultSqlMembershipSourceAttributes(newSqlMembershipAttributes ?? [])
      );
  
      dispatch(
        setAttributes(newSqlMembershipAttributes)
      );
    }
    // there is a Toast notification in fluent/react-components (v9) that we should be using for save notifications.
  };

  if (!canViewSettings) {
    return (<MessageBar
      messageBarType={MessageBarType.error}
      isMultiline={false}
    >
    {strings.Errors.forbidden}
  </MessageBar>);
}
  // render the view with the data from the store and the event handler
  return (
    <AdminConfigView
      {...props}
      isSaving={areSettingsSaving || isSourceSaving || areAttributesSaving}
      settings={settings}
      strings={strings}
      onSave={handleSave}
      handleGetValues={handleGetValues}
      sqlMembershipSource={sqlMembershipSource}
      sqlMembershipSourceAttributes={sqlMembershipSourceAttributes}
      isHyperlinkAdmin={isHyperlinkAdmin}
      isCustomMembershipProviderAdmin={isCustomMembershipProviderAdmin}
      isOperationsResetAdministrator={isOperationsResetAdministrator}
      isGeneralSettingsAdministrator={isGeneralSettingsAdministrator}
    />
  );
};
