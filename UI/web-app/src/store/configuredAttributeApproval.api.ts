// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { ThunkConfigWithErrors } from './store';
import {
  ConfiguredAttributeApprovalConfiguration,
  ConfiguredAttributeApprovalError,
  toConfiguredAttributeApprovalError,
} from '../apis/configuredAttributeApproval';

type Config = ThunkConfigWithErrors<ConfiguredAttributeApprovalError>;

export const fetchConfiguredAttributeApproval = createAsyncThunk<ConfiguredAttributeApprovalConfiguration, void, Config>(
  'configuredAttributeApproval/fetchConfiguration',
  async (_, { extra, rejectWithValue }) => {
    try {
      return await extra.apis.gmmApi.configuredAttributeApproval.fetchConfiguration();
    } catch (error) {
      return rejectWithValue(toConfiguredAttributeApprovalError(error));
    }
  }
);

export const saveConfiguredAttributeApproval = createAsyncThunk<ConfiguredAttributeApprovalConfiguration, ConfiguredAttributeApprovalConfiguration, Config>(
  'configuredAttributeApproval/saveConfiguration',
  async (configuration, { extra, rejectWithValue }) => {
    try {
      return await extra.apis.gmmApi.configuredAttributeApproval.saveConfiguration(configuration);
    } catch (error) {
      return rejectWithValue(toConfiguredAttributeApprovalError(error));
    }
  }
);
