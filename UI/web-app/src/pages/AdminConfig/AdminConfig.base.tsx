// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { AdminConfigProps } from './AdminConfig.types';
import {
  selectDashboardUrl,
  selectIsSaving,
  selectOutlookWarningUrl,
  selectPrivacyPolicyUrl,
} from '../../store/settings.slice';
import { patchSetting, fetchSettings } from '../../store/settings.api';
import { AppDispatch } from '../../store';
import { AdminConfigView } from './AdminConfig.view';
import { SettingName } from '../../models';
import { useStrings } from '../../store/hooks';
import { SettingKey } from '../../models/SettingKey';
import { setPagingBarVisible } from '../../store/pagingBar.slice';


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
  const isSaving = useSelector(selectIsSaving);
  const strings = useStrings().AdminConfig;

  // Create an event handler that should be called when the user clicks the save button.
  const handleSave = (settings: { readonly [key in SettingName]: string }) => {
    // should be saving all settings here, not one at a time.
    new Promise(() => {
      dispatch(
        patchSetting({
          settingKey: SettingKey.DashboardUrl,
          settingName: SettingName.DashboardUrl,
          settingValue: settings[SettingName.DashboardUrl],
        })
      );
      dispatch(patchSetting({
        settingKey: SettingKey.OutlookWarningUrl,
        settingName: SettingName.OutlookWarningUrl,
        settingValue: settings[SettingName.OutlookWarningUrl]
      }));
      dispatch(
        patchSetting({
          settingKey: SettingKey.PrivacyPolicyUrl,
          settingName: SettingName.PrivacyPolicyUrl,
          settingValue: settings[SettingName.PrivacyPolicyUrl],
        })
      );
    })
    .then(() => {
      dispatch(fetchSettings());
    });
    // there is a Toast notification in fluent/react-components (v9) that we should be using for save notifications.
  };

  // render the view with the data from the store and the event handler

  return (
    <AdminConfigView
      {...props}
      isSaving={isSaving}
      settings={{
        [SettingName.DashboardUrl]: dashboardUrl ?? '',
        [SettingName.OutlookWarningUrl]: outlookWarningUrl ?? '',
        [SettingName.PrivacyPolicyUrl]: privacyPolicyUrl ?? '',
      }}
      strings={strings}
      onSave={handleSave}
    />
  );
};
