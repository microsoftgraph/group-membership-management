// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';
import { fetchAttributeMappings, fetchAttributeValues, fetchDefaultSqlMembershipSource, fetchDefaultSqlMembershipSourceAttributes, patchDefaultSqlMembershipSourceAttributes, patchDefaultSqlMembershipSourceCustomLabel } from './sqlMembershipSources.api';
import type { RootState } from './store';
import { SqlMembershipAttribute, SqlMembershipAttributeMapping, SqlMembershipSource } from '../models';

export interface SettingsState {

  source: SqlMembershipSource | undefined;
  attributes: SqlMembershipAttribute[] | undefined;
  isSourceLoading: boolean;
  areAttributesLoading: boolean;
  areAttributeMappingsLoading: boolean;
  attributeMappings: {
    [attribute: string]: {
      mappings: SqlMembershipAttributeMapping[];
      type: string | undefined;
    }
  };
  isSourceSaving: boolean;
  areAttributesSaving: boolean;
  error: string | undefined;
  patchResponse: any | undefined;
  patchError: string | undefined;
};

const initialState: SettingsState = {
  source: undefined,
  attributes: undefined,
  attributeMappings: {},
  isSourceLoading: false,
  areAttributesLoading: false,
  areAttributeMappingsLoading: false,
  isSourceSaving: false,
  areAttributesSaving: false,
  error: undefined,
  patchResponse: undefined,
  patchError: undefined
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
    setAttributeMappings: (state, action) => {
      const { attribute, type, mappings} = action.payload;
      state.attributeMappings[attribute] = {
        mappings: mappings,
        type: type
      };
    }
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

    builder.addCase(fetchAttributeMappings.pending, (state) => {
      state.areAttributeMappingsLoading = true;
    });
    builder.addCase(fetchAttributeMappings.fulfilled, (state, action) => {
      state.areAttributeMappingsLoading = false;
      const { attribute, type, mappings} = action.payload;
      state.attributeMappings[attribute] = {
        mappings: mappings,
        type: type
      };
    });
    builder.addCase(fetchAttributeMappings.rejected, (state, action) => {
      state.areAttributeMappingsLoading = false;
      state.error = action.error.message;
    });

    builder.addCase(fetchAttributeValues.fulfilled, (state, action) => {
      state.areAttributeMappingsLoading = false;
      const { attribute, values} = action.payload;
      state.attributes = state.attributes?.map(attr => 
        attr.name === attribute ? { ...attr, values: values } : attr
      );
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

export const { setSource, setAttributes, setAttributeMappings } = sqlMembershipSourcesSlice.actions;
export const selectSource = (state: RootState) => state.sqlMembershipSources.source;
export const selectAttributes = (state: RootState) => state.sqlMembershipSources.attributes;
export const selectAttributeMappings = (state: RootState) => state.sqlMembershipSources.attributeMappings;
export const selectIsSourceLoading = (state: RootState) => state.sqlMembershipSources.isSourceLoading;
export const selectAreAttributesLoading = (state: RootState) => state.sqlMembershipSources.areAttributesLoading;
export const selectAreAttributeMappingsLoading = (state: RootState) => state.sqlMembershipSources.areAttributeMappingsLoading;
export const selectIsSourceSaving = (state: RootState) => state.sqlMembershipSources.isSourceSaving;
export const selectAreAttributesSaving = (state: RootState) => state.sqlMembershipSources.areAttributesSaving;

export default sqlMembershipSourcesSlice.reducer;
