// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice } from '@reduxjs/toolkit';
import type { RootState } from './store';
import { getIsAdmin } from './roles.api';

// Define a type for the slice state
export type Roles = {
  isAdmin?: boolean;
}

// Define the initial state using that type
const initialState: Roles = {
  isAdmin: false,
};

export const rolesSlice = createSlice({
  name: 'roles',
  initialState,
  reducers: { },
  extraReducers: (builder) => {
    builder.addCase(getIsAdmin.fulfilled, (state, action) => {
      state.isAdmin = action.payload;
    });
  }
});

export const selectIsAdmin = (state: RootState) => state.roles.isAdmin;
export default rolesSlice.reducer;