// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { ThunkConfig } from './store';

export const getIsAdmin = createAsyncThunk<boolean, void, ThunkConfig>(
  'roles/getIsAdmin',
  async (_, { extra }) => {
    const { gmmApi } = extra.apis;
    try {
      return await gmmApi.roles.getIsAdmin();
    } catch (error) {
      throw new Error('Failed to call getIsAdmin endpoint');
    }
  }
);

export const getIsSubmissionReviewer = createAsyncThunk<boolean, void, ThunkConfig>(
  'roles/getIsSubmissionReviewer',
  async (_, { extra }) => {
    const { gmmApi } = extra.apis;
    try {
      return await gmmApi.roles.getIsSubmissionReviewer();
    } catch (error) {
      throw new Error('Failed to call getIsSubmissionReviewer endpoint');
    }
  }
);


export const getIsTenantJobEditor = createAsyncThunk<boolean, void, ThunkConfig>(
  'roles/getIsTenantJobEditor',
  async (_, { extra }) => {
    const { gmmApi } = extra.apis;
    try {
      return await gmmApi.roles.getIsTenantJobEditor();
    } catch (error) {
      throw new Error('Failed to call getIsTenantJobEditor endpoint');
    }
  }
);
