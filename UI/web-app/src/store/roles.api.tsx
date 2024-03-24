// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { ThunkConfig } from './store';
import { Roles } from '../apis/roles/IRolesApi';

export const getAllRoles = createAsyncThunk<Roles, void, ThunkConfig>(
  'roles/getAllRoles',
  async (_, { extra }) => {
    const { gmmApi } = extra.apis;
    try {
      return await gmmApi.roles.getAllRoles();
    } catch (error) {
      throw new Error('Failed to call getAllRoles endpoint');
    }
  }
);