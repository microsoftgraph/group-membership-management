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
} from '../../store/settings.slice';
import { patchSetting } from '../../store/settings.api';
import { AppDispatch } from '../../store';
import { AdminConfigView } from './AdminConfig.view';
import { useStrings } from '../../store/hooks';
import { SettingKey } from '../../models/SettingKey';
import { setPagingBarVisible } from '../../store/pagingBar.slice';
import { selectSource, selectAttributes, selectIsSourceSaving, selectAreAttributesSaving, setSource, setAttributes } from '../../store/sqlMembershipSources.slice';
import { SqlMembershipAttribute, SqlMembershipSource } from '../../models';
import { patchDefaultSqlMembershipSourceAttributes, patchDefaultSqlMembershipSourceCustomLabel } from '../../store/sqlMembershipSources.api';
import { selectIsCustomMembershipProviderAdministrator, selectIsHyperlinkAdministrator } from '../../store/roles.slice';


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
  const sqlMembershipSource = useSelector(selectSource);
  const sqlMembershipSourceAttributes = useSelector(selectAttributes);
  const isSourceSaving = useSelector(selectIsSourceSaving);
  const areAttributesSaving = useSelector(selectAreAttributesSaving);
  const areSettingsSaving = useSelector(selectIsSaving);
  const isHyperlinkAdmin = useSelector(selectIsHyperlinkAdministrator);
  const isCustomMembershipProviderAdmin = useSelector(selectIsCustomMembershipProviderAdministrator);
  const strings = useStrings().AdminConfig;

  const generateSettings = () => ({
    [SettingKey.DashboardUrl]: dashboardUrl ?? '',
    [SettingKey.OutlookWarningUrl]: outlookWarningUrl ?? '',
    [SettingKey.PrivacyPolicyUrl]: privacyPolicyUrl ?? ''
  });

  const [settings, setSettings] = useState<{ readonly [key in SettingKey]: string }>(generateSettings());

  useEffect(() => { 
    setSettings(generateSettings())
  }, [dashboardUrl, outlookWarningUrl, privacyPolicyUrl]);

  // Create an event handler that should be called when the user clicks the save button.
  const handleSave = (newSettings: { readonly [key in SettingKey]: string }, newSqlMembershipSource: SqlMembershipSource | undefined, newSqlMembershipAttributes: SqlMembershipAttribute[] | undefined) => {
   
    // save new settings
    if (JSON.stringify(newSettings) !== JSON.stringify(settings)) {

      setSettings(newSettings);

      dispatch(
        patchSetting({
          settingKey: SettingKey.DashboardUrl,
          settingValue: newSettings[SettingKey.DashboardUrl],
        })
      );
      dispatch(patchSetting({
        settingKey: SettingKey.OutlookWarningUrl,
        settingValue: newSettings[SettingKey.OutlookWarningUrl]
      }));
      dispatch(
        patchSetting({
          settingKey: SettingKey.PrivacyPolicyUrl,
          settingValue: newSettings[SettingKey.PrivacyPolicyUrl],
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

  // render the view with the data from the store and the event handler
  return (
    <AdminConfigView
      {...props}
      isSaving={areSettingsSaving || isSourceSaving || areAttributesSaving}
      settings={settings}
      strings={strings}
      onSave={handleSave}
      sqlMembershipSource={sqlMembershipSource}
      sqlMembershipSourceAttributes={sqlMembershipSourceAttributes}
      isHyperlinkAdmin={isHyperlinkAdmin}
      isCustomMembershipProviderAdmin={isCustomMembershipProviderAdmin}
    />
  );
};
