// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createSlice, type PayloadAction } from '@reduxjs/toolkit';
import { fetchAttributeMappings, fetchAttributeValues, fetchDefaultSqlMembershipSource, fetchDefaultSqlMembershipSourceAttributes, patchDefaultSqlMembershipSourceAttributes, patchDefaultSqlMembershipSourceCustomLabel, resolveAttributeMappings } from './sqlMembershipSources.api';
import type { RootState } from './store';
import { SqlMembershipAttribute, SqlMembershipAttributeMapping, SqlMembershipSource } from '../models';

export interface AttributeMappingsEntry {
  /** Union of the current browse/search page and every explicitly resolved (pinned) code. */
  mappings: SqlMembershipAttributeMapping[];
  type: string | undefined;
  /** Server reported more rows than were returned, so the list is not exhaustive. */
  hasMore: boolean;
  /** Last page returned by the server for the current search term. */
  page: SqlMembershipAttributeMapping[];
  /** Codes resolved explicitly because they are already selected on a saved job. */
  pinned: SqlMembershipAttributeMapping[];
  /** Search term the current page was fetched with. */
  search: string | undefined;
  isSearching: boolean;
};

export interface SettingsState {

  source: SqlMembershipSource | undefined;
  attributes: SqlMembershipAttribute[] | undefined;
  isSourceLoading: boolean;
  areAttributesLoading: boolean;
  areAttributeMappingsLoading: boolean;
  attributeMappings: {
    [attribute: string]: AttributeMappingsEntry
  };
  /** Latest in-flight fetch per attribute; older responses are discarded so search results can't arrive out of order. */
  attributeMappingsRequestIds: { [attribute: string]: string };
  isSourceSaving: boolean;
  areAttributesSaving: boolean;
  error: string | undefined;
  patchResponse: any | undefined;
  patchError: string | undefined;
};

const emptyEntry = (type: string | undefined): AttributeMappingsEntry => ({
  mappings: [],
  type,
  hasMore: false,
  page: [],
  pinned: [],
  search: undefined,
  isSearching: false
});

// Pinned codes must survive every search so a selected value is never dropped from the query.
const mergeMappings = (page: SqlMembershipAttributeMapping[], pinned: SqlMembershipAttributeMapping[]): SqlMembershipAttributeMapping[] => {
  const byCode = new Map<string, SqlMembershipAttributeMapping>();
  pinned.forEach(mapping => byCode.set(mapping.code, mapping));
  page.forEach(mapping => byCode.set(mapping.code, mapping));
  return Array.from(byCode.values()).sort((a, b) => (a.description ?? '').localeCompare(b.description ?? ''));
};

const sameCodeSequence = (a: SqlMembershipAttributeMapping[], b: SqlMembershipAttributeMapping[]): boolean =>
  a.length === b.length && a.every((mapping, i) => mapping.code === b[i].code);

const pinMappings = (state: SettingsState, attribute: string, type: string | undefined, mappings: SqlMembershipAttributeMapping[]) => {
  const entry = state.attributeMappings[attribute] ?? emptyEntry(type);
  const pinnedByCode = new Map<string, SqlMembershipAttributeMapping>();
  entry.pinned.forEach(mapping => pinnedByCode.set(mapping.code, mapping));

  // Nothing new to record: leave every reference untouched so the combobox does not see a
  // fresh `options` array. Re-pinning happens on every pick, and a new array identity
  // mid-interaction closes an open multi-select (IN) menu.
  const alreadyPinned = mappings.every(mapping => {
    const existing = pinnedByCode.get(mapping.code);
    return existing !== undefined && existing.description === mapping.description;
  });
  if (alreadyPinned) {
    return;
  }

  mappings.forEach(mapping => pinnedByCode.set(mapping.code, mapping));
  entry.pinned = Array.from(pinnedByCode.values());
  const merged = mergeMappings(entry.page, entry.pinned);
  // Only swap the visible list when its contents actually changed, for the same reason.
  if (!sameCodeSequence(merged, entry.mappings)) {
    entry.mappings = merged;
  }
  state.attributeMappings[attribute] = entry;
};

const initialState: SettingsState = {
  source: undefined,
  attributes: undefined,
  attributeMappings: {},
  attributeMappingsRequestIds: {},
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
      const entry = state.attributeMappings[attribute] ?? emptyEntry(type);
      entry.type = type;
      entry.page = mappings;
      entry.mappings = mergeMappings(mappings, entry.pinned);
      state.attributeMappings[attribute] = entry;
    },
    // Pins already-loaded mappings for selected codes so a later server search can't drop them.
    pinAttributeMappings: (state, action: PayloadAction<{ attribute: string; type: string | undefined; mappings: SqlMembershipAttributeMapping[] }>) => {
      const { attribute, type, mappings } = action.payload;
      if (mappings.length === 0) {
        return;
      }
      pinMappings(state, attribute, type, mappings);
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

    builder.addCase(fetchAttributeMappings.pending, (state, action) => {
      state.areAttributeMappingsLoading = true;
      const attribute = action.meta.arg?.attribute;
      if (!attribute) {
        return;
      }
      state.attributeMappingsRequestIds[attribute] = action.meta.requestId;
      const entry = state.attributeMappings[attribute];
      if (entry) {
        entry.isSearching = true;
      }
    });
    builder.addCase(fetchAttributeMappings.fulfilled, (state, action) => {
      const { attribute, type, mappings, hasMore, search } = action.payload;
      const latestRequestId = state.attributeMappingsRequestIds[attribute];
      // A slower earlier search must never overwrite the results of a newer one.
      if (latestRequestId && latestRequestId !== action.meta.requestId) {
        return;
      }
      state.areAttributeMappingsLoading = false;
      const entry = state.attributeMappings[attribute] ?? emptyEntry(type);
      entry.type = type;
      entry.hasMore = hasMore;
      entry.search = search;
      entry.isSearching = false;
      entry.page = mappings;
      entry.mappings = mergeMappings(mappings, entry.pinned);
      state.attributeMappings[attribute] = entry;
    });
    builder.addCase(fetchAttributeMappings.rejected, (state, action) => {
      const attribute = action.meta.arg?.attribute;
      const latestRequestId = attribute ? state.attributeMappingsRequestIds[attribute] : undefined;
      if (latestRequestId && latestRequestId !== action.meta.requestId) {
        return;
      }
      state.areAttributeMappingsLoading = false;
      const entry = attribute ? state.attributeMappings[attribute] : undefined;
      if (entry) {
        entry.isSearching = false;
      }
      state.error = action.error.message;
    });

    builder.addCase(resolveAttributeMappings.fulfilled, (state, action) => {
      const { attribute, type, mappings } = action.payload;
      if (mappings.length === 0) {
        return;
      }
      pinMappings(state, attribute, type, mappings);
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

export const { setSource, setAttributes, setAttributeMappings, pinAttributeMappings } = sqlMembershipSourcesSlice.actions;
export const selectSource = (state: RootState) => state.sqlMembershipSources.source;
export const selectAttributes = (state: RootState) => state.sqlMembershipSources.attributes;
export const selectAttributeMappings = (state: RootState) => state.sqlMembershipSources.attributeMappings;
export const selectIsSourceLoading = (state: RootState) => state.sqlMembershipSources.isSourceLoading;
export const selectAreAttributesLoading = (state: RootState) => state.sqlMembershipSources.areAttributesLoading;
export const selectAreAttributeMappingsLoading = (state: RootState) => state.sqlMembershipSources.areAttributeMappingsLoading;
export const selectIsSourceSaving = (state: RootState) => state.sqlMembershipSources.isSourceSaving;
export const selectAreAttributesSaving = (state: RootState) => state.sqlMembershipSources.areAttributesSaving;

export default sqlMembershipSourcesSlice.reducer;
