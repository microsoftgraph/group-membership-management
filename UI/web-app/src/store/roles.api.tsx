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
