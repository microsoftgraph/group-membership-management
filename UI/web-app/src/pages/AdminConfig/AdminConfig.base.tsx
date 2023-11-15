// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { AdminConfigProps } from './AdminConfig.types';
import { selectIsSaving, selectSelectedSetting } from '../../store/settings.slice';
import { fetchSettingByKey, updateSetting } from '../../store/settings.api';
import { AppDispatch } from '../../store';
import { AdminConfigView } from './AdminConfig.view';
import { SettingName } from '../../models';
import { useStrings } from '../../store/hooks';

export const AdminConfigBase: React.FunctionComponent<AdminConfigProps> = (props: AdminConfigProps) => {
  // get the store's dispatch function
  const dispatch = useDispatch<AppDispatch>();

  // Tell the store to retrieve the most recent settings data once the component mounts.
  useEffect(() => {
    // we should probably retrieve all settings here and put them in the state, instead of retrieving them individually.
    dispatch(fetchSettingByKey(SettingName.DashboardUrl));
  }, [dispatch]);

  // get the settings data from the store
  const dashboardUrl = useSelector(selectSelectedSetting);
  const isSaving = useSelector(selectIsSaving);
  const strings = useStrings().AdminConfig;

  // Create an event handler that should be called when the user clicks the save button.
  const handleSave = (settings: { readonly [key in SettingName]: string }) => {
    // should be saving all settings here, not one at a time.
    dispatch(updateSetting({ key: SettingName.DashboardUrl, value: settings[SettingName.DashboardUrl] }));
    // there is a Toast notification in fluent/react-components (v9) that we should be using for save notifications.
  };

  // render the view with the data from the store and the event handler
  
  return (
    <AdminConfigView
      {...props}
      isSaving={isSaving}
      settings={{
        [SettingName.DashboardUrl]: dashboardUrl?.value ?? '',
      }}
      strings={strings}
      onSave={handleSave}
    />
  );
};
