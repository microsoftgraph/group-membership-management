// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';
import { fetchDefaultSqlMembershipSource, fetchDefaultSqlMembershipSourceAttributes, patchDefaultSqlMembershipSourceAttributes, patchDefaultSqlMembershipSourceCustomLabel } from './sqlMembershipSources.api';
import type { RootState } from './store';
import { SqlMembershipAttribute, SqlMembershipSource } from '../models';

export interface SettingsState {

  source: SqlMembershipSource | undefined;
  attributes: SqlMembershipAttribute[] | undefined;
  isSourceLoading: boolean;
  areAttributesLoading: boolean;
  isSourceSaving: boolean;
  areAttributesSaving: boolean;
  error: string | undefined;
  patchResponse: any | undefined;
  patchError: string | undefined;
}

const initialState: SettingsState = {
  source: undefined,
  attributes: undefined,
  isSourceLoading: false,
  areAttributesLoading: false,
  isSourceSaving: false,
  areAttributesSaving: false,
  error: undefined,
  patchResponse: undefined,
  patchError: undefined,
};

const sqlMembershipSourcesSlice = createSlice({
  name: 'sqlMembershipSources',
  initialState,
  reducers: {
    setSource: (state, action: PayloadAction<SqlMembershipSource | undefined>) => {
        state.source = action.payload;
    },
    setAttributes: (state, action: PayloadAction<SqlMembershipAttribute[] | undefined>) => {
        state.attributes = action.payload;
    },
  },
  extraReducers: (builder) => {

    builder.addCase(fetchDefaultSqlMembershipSource.pending, (state) => {
      state.isSourceLoading = true;
    });
    builder.addCase(fetchDefaultSqlMembershipSource.fulfilled, (state, action) => {
      state.isSourceLoading = false;
      state.source = action.payload;
    });
    builder.addCase(fetchDefaultSqlMembershipSource.rejected, (state, action) => {
      state.isSourceLoading = false;
      state.error = action.error.message;
    });

    builder.addCase(fetchDefaultSqlMembershipSourceAttributes.pending, (state) => {
      state.areAttributesLoading = true;
    });
    builder.addCase(fetchDefaultSqlMembershipSourceAttributes.fulfilled, (state, action) => {
      state.areAttributesLoading = false;
      state.attributes = action.payload;
    });
    builder.addCase(fetchDefaultSqlMembershipSourceAttributes.rejected, (state, action) => {
      state.areAttributesLoading = false;
      state.error = action.error.message;
    });

    builder.addCase(patchDefaultSqlMembershipSourceCustomLabel.pending, (state) => {
      state.isSourceSaving = true;
    });
    builder.addCase(patchDefaultSqlMembershipSourceCustomLabel.fulfilled, (state, action) => {
      state.isSourceSaving = false;
      state.patchResponse = action.payload;
      state.patchError = undefined;
    });
    builder.addCase(patchDefaultSqlMembershipSourceCustomLabel.rejected, (state, action) => {
      state.isSourceSaving = false;
      state.error = action.error.message;
      state.patchResponse = action.payload;
      state.patchError = action.error.message;
    });

    builder.addCase(patchDefaultSqlMembershipSourceAttributes.pending, (state) => {
      state.areAttributesSaving = true;
    });
    builder.addCase(patchDefaultSqlMembershipSourceAttributes.fulfilled, (state, action) => {
      state.areAttributesSaving = false;
      state.patchResponse = action.payload;
      state.patchError = undefined;
    });
    builder.addCase(patchDefaultSqlMembershipSourceAttributes.rejected, (state, action) => {
      state.areAttributesSaving = false;
      state.error = action.error.message;
      state.patchResponse = action.payload;
      state.patchError = action.error.message;
    });
    
  },
});

export const { setSource, setAttributes } = sqlMembershipSourcesSlice.actions;

export const selectSource = (state: RootState) => state.sqlMembershipSources.source;
export const selectAttributes = (state: RootState) => state.sqlMembershipSources.attributes;
export const selectIsSourceLoading = (state: RootState) => state.sqlMembershipSources.isSourceLoading;
export const selectAreAttributesLoading = (state: RootState) => state.sqlMembershipSources.areAttributesLoading;
export const selectIsSourceSaving = (state: RootState) => state.sqlMembershipSources.isSourceSaving;
export const selectAreAttributesSaving = (state: RootState) => state.sqlMembershipSources.areAttributesSaving;

export default sqlMembershipSourcesSlice.reducer;
