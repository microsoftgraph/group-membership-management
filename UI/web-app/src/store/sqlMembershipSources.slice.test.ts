// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import sqlReducer, {
  setSource,
  setAttributes,
  setAttributeMappings,
  pinAttributeMappings,
  selectSource,
  selectAttributes,
  selectAttributeMappings,
  selectIsSourceLoading,
  selectAreAttributesLoading,
  selectAreAttributeMappingsLoading,
  selectIsSourceSaving,
  selectAreAttributesSaving,
} from './sqlMembershipSources.slice';
import {
  fetchDefaultSqlMembershipSource,
  fetchDefaultSqlMembershipSourceAttributes,
  fetchAttributeMappings,
  resolveAttributeMappings,
  fetchAttributeValues,
  patchDefaultSqlMembershipSourceCustomLabel,
  patchDefaultSqlMembershipSourceAttributes,
} from './sqlMembershipSources.api';

const initial = sqlReducer(undefined, { type: '@@INIT' });

describe('sqlMembershipSources.slice — reducers', () => {
  it('setSource sets source', () => {
    const src = { name: 'src1' } as any;
    expect(sqlReducer(initial, setSource(src)).source).toEqual(src);
  });

  it('setAttributes sets attributes', () => {
    const attrs = [{ name: 'a1' }] as any;
    expect(sqlReducer(initial, setAttributes(attrs)).attributes).toEqual(attrs);
  });

  it('setAttributeMappings sets mapping for attribute', () => {
    const state = sqlReducer(initial, setAttributeMappings({ attribute: 'dept', type: 'string', mappings: [{ code: 'eng', description: 'Engineering' }] }));
    expect(state.attributeMappings['dept']).toEqual({
      mappings: [{ code: 'eng', description: 'Engineering' }],
      page: [{ code: 'eng', description: 'Engineering' }],
      pinned: [],
      type: 'string',
      hasMore: false,
      search: undefined,
      isSearching: false,
    });
  });
  it('pinAttributeMappings keeps codes across a later search', () => {
    const loaded = sqlReducer(initial, fetchAttributeMappings.fulfilled({ attribute: 'CostCenter_Code', type: 'nvarchar', mappings: [{ code: '1', description: 'Alpha' }], hasMore: true } as any, 'r', {} as any));
    const pinned = sqlReducer(loaded, pinAttributeMappings({ attribute: 'CostCenter_Code', type: 'nvarchar', mappings: [{ code: '1', description: 'Alpha' }] }));
    const searched = sqlReducer(pinned, fetchAttributeMappings.fulfilled({ attribute: 'CostCenter_Code', type: 'nvarchar', mappings: [{ code: '2', description: 'Beta' }], hasMore: false, search: 'B' } as any, 'r', {} as any));
    expect(searched.attributeMappings['CostCenter_Code'].mappings.map(m => m.code).sort()).toEqual(['1', '2']);
  });
  // Regression: re-pinning an unchanged code must not hand the combobox a new `options` array.
  // A fresh array identity mid-interaction closes an open multi-select (IN) dropdown, so the
  // owner could only ever check one value before the menu snapped shut.
  it('pinAttributeMappings keeps mappings referentially stable when nothing changed', () => {
    const loaded = sqlReducer(initial, fetchAttributeMappings.fulfilled({ attribute: 'EmployeeType_Code', type: 'nvarchar', mappings: [{ code: 'Vendor', description: 'Vendor' }, { code: 'FTE', description: 'FTE' }], hasMore: false } as any, 'r', {} as any));
    const first = sqlReducer(loaded, pinAttributeMappings({ attribute: 'EmployeeType_Code', type: 'nvarchar', mappings: [{ code: 'Vendor', description: 'Vendor' }] }));
    const second = sqlReducer(first, pinAttributeMappings({ attribute: 'EmployeeType_Code', type: 'nvarchar', mappings: [{ code: 'Vendor', description: 'Vendor' }] }));
    expect(second.attributeMappings['EmployeeType_Code'].mappings).toBe(first.attributeMappings['EmployeeType_Code'].mappings);
    expect(second.attributeMappings['EmployeeType_Code'].mappings.map(m => m.code).sort()).toEqual(['FTE', 'Vendor']);
  });
  it('pinAttributeMappings still records a code that is not yet in the page', () => {
    const loaded = sqlReducer(initial, fetchAttributeMappings.fulfilled({ attribute: 'CostCenter_Code', type: 'nvarchar', mappings: [{ code: '1', description: 'Alpha' }], hasMore: true } as any, 'r', {} as any));
    const pinned = sqlReducer(loaded, pinAttributeMappings({ attribute: 'CostCenter_Code', type: 'nvarchar', mappings: [{ code: '9', description: 'Zeta' }] }));
    expect(pinned.attributeMappings['CostCenter_Code'].pinned.map(m => m.code)).toEqual(['9']);
    expect(pinned.attributeMappings['CostCenter_Code'].mappings.map(m => m.code).sort()).toEqual(['1', '9']);
  });
});

describe('sqlMembershipSources.slice — fetchDefaultSqlMembershipSource', () => {
  it('pending sets loading', () => {
    const state = sqlReducer(initial, fetchDefaultSqlMembershipSource.pending('r', undefined as any));
    expect(state.isSourceLoading).toBe(true);
  });
  it('fulfilled sets source', () => {
    const src = { name: 'x' } as any;
    const state = sqlReducer(initial, fetchDefaultSqlMembershipSource.fulfilled(src, 'r', undefined as any));
    expect(state.isSourceLoading).toBe(false);
    expect(state.source).toEqual(src);
  });
  it('rejected sets error', () => {
    const state = sqlReducer(initial, fetchDefaultSqlMembershipSource.rejected(new Error('e'), 'r', undefined as any));
    expect(state.isSourceLoading).toBe(false);
    expect(state.error).toBe('e');
  });
});

describe('sqlMembershipSources.slice — fetchDefaultSqlMembershipSourceAttributes', () => {
  it('pending sets loading', () => {
    const state = sqlReducer(initial, fetchDefaultSqlMembershipSourceAttributes.pending('r', undefined as any));
    expect(state.areAttributesLoading).toBe(true);
  });
  it('fulfilled sets attributes', () => {
    const attrs = [{ name: 'a' }] as any;
    const state = sqlReducer(initial, fetchDefaultSqlMembershipSourceAttributes.fulfilled(attrs, 'r', undefined as any));
    expect(state.areAttributesLoading).toBe(false);
    expect(state.attributes).toEqual(attrs);
  });
  it('rejected sets error', () => {
    const state = sqlReducer(initial, fetchDefaultSqlMembershipSourceAttributes.rejected(new Error('err'), 'r', undefined as any));
    expect(state.areAttributesLoading).toBe(false);
    expect(state.error).toBe('err');
  });
});

describe('sqlMembershipSources.slice — fetchAttributeMappings', () => {
  it('pending sets loading', () => {
    const state = sqlReducer(initial, fetchAttributeMappings.pending('r', {} as any));
    expect(state.areAttributeMappingsLoading).toBe(true);
  });
  it('fulfilled sets mapping', () => {
    const payload = { attribute: 'dept', type: 'string', mappings: [{ code: '1', description: 'Eng' }], hasMore: false };
    const state = sqlReducer(initial, fetchAttributeMappings.fulfilled(payload as any, 'r', {} as any));
    expect(state.areAttributeMappingsLoading).toBe(false);
    expect(state.attributeMappings['dept'].mappings).toEqual([{ code: '1', description: 'Eng' }]);
    expect(state.attributeMappings['dept'].type).toBe('string');
    expect(state.attributeMappings['dept'].hasMore).toBe(false);
  });
  it('fulfilled records hasMore and the search term for capped attributes', () => {
    const payload = { attribute: 'CostCenter_Code', type: 'nvarchar', mappings: [{ code: '1', description: 'A' }], hasMore: true, search: 'A' };
    const state = sqlReducer(initial, fetchAttributeMappings.fulfilled(payload as any, 'r', {} as any));
    expect(state.attributeMappings['CostCenter_Code'].hasMore).toBe(true);
    expect(state.attributeMappings['CostCenter_Code'].search).toBe('A');
    expect(state.attributeMappings['CostCenter_Code'].isSearching).toBe(false);
  });
  it('pending flags an existing attribute as searching', () => {
    const loaded = sqlReducer(initial, fetchAttributeMappings.fulfilled({ attribute: 'dept', type: 'string', mappings: [], hasMore: true } as any, 'r', {} as any));
    const state = sqlReducer(loaded, fetchAttributeMappings.pending('r', { attribute: 'dept' } as any));
    expect(state.attributeMappings['dept'].isSearching).toBe(true);
  });
  it('keeps resolved codes when a later search does not return them', () => {
    const loaded = sqlReducer(initial, fetchAttributeMappings.fulfilled({ attribute: 'CostCenter_Code', type: 'nvarchar', mappings: [{ code: '1', description: 'Alpha' }], hasMore: true } as any, 'r', {} as any));
    const resolved = sqlReducer(loaded, resolveAttributeMappings.fulfilled({ attribute: 'CostCenter_Code', type: 'nvarchar', mappings: [{ code: '99', description: 'Saved Cost Center' }] } as any, 'r', {} as any));
    expect(resolved.attributeMappings['CostCenter_Code'].mappings.map(m => m.code).sort()).toEqual(['1', '99']);

    const searched = sqlReducer(resolved, fetchAttributeMappings.fulfilled({ attribute: 'CostCenter_Code', type: 'nvarchar', mappings: [{ code: '2', description: 'Beta' }], hasMore: false, search: 'B' } as any, 'r', {} as any));
    expect(searched.attributeMappings['CostCenter_Code'].mappings.map(m => m.code).sort()).toEqual(['2', '99']);
    expect(searched.attributeMappings['CostCenter_Code'].page.map(m => m.code)).toEqual(['2']);
  });
  it('ignores a stale fulfilled that lands after a newer request started', () => {
    const arg = { attribute: 'CostCenter_Code' } as any;
    let state = sqlReducer(initial, fetchAttributeMappings.pending('req-1', arg));
    state = sqlReducer(state, fetchAttributeMappings.pending('req-2', arg));

    // "AL" was typed first but its response arrives last; it must not clobber "ALP".
    state = sqlReducer(state, fetchAttributeMappings.fulfilled({ attribute: 'CostCenter_Code', type: 'nvarchar', mappings: [{ code: '2', description: 'Alpha Two' }], hasMore: false, search: 'ALP' } as any, 'req-2', arg));
    state = sqlReducer(state, fetchAttributeMappings.fulfilled({ attribute: 'CostCenter_Code', type: 'nvarchar', mappings: [{ code: '1', description: 'Alpha One' }], hasMore: true, search: 'AL' } as any, 'req-1', arg));

    expect(state.attributeMappings['CostCenter_Code'].search).toBe('ALP');
    expect(state.attributeMappings['CostCenter_Code'].mappings.map(m => m.code)).toEqual(['2']);
    expect(state.attributeMappings['CostCenter_Code'].hasMore).toBe(false);
  });
  it('ignores a stale rejected that lands after a newer request started', () => {
    const arg = { attribute: 'CostCenter_Code' } as any;
    let state = sqlReducer(initial, fetchAttributeMappings.pending('req-1', arg));
    state = sqlReducer(state, fetchAttributeMappings.pending('req-2', arg));
    state = sqlReducer(state, fetchAttributeMappings.rejected(new Error('stale'), 'req-1', arg));

    expect(state.error).toBeUndefined();
    expect(state.areAttributeMappingsLoading).toBe(true);
  });
  it('rejected sets error', () => {
    const state = sqlReducer(initial, fetchAttributeMappings.rejected(new Error('x'), 'r', {} as any));
    expect(state.areAttributeMappingsLoading).toBe(false);
    expect(state.error).toBe('x');
  });
});

describe('sqlMembershipSources.slice — fetchAttributeValues', () => {
  it('fulfilled updates attribute values', () => {
    const seeded = { ...initial, attributes: [{ name: 'dept', values: [] }, { name: 'loc', values: [] }] as any };
    const payload = { attribute: 'dept', values: ['eng', 'sales'] };
    const state = sqlReducer(seeded, fetchAttributeValues.fulfilled(payload as any, 'r', {} as any));
    const dept = state.attributes?.find(a => a.name === 'dept');
    expect(dept?.values).toEqual(['eng', 'sales']);
    const loc = state.attributes?.find(a => a.name === 'loc');
    expect(loc?.values).toEqual([]);
  });
});

describe('sqlMembershipSources.slice — patchDefaultSqlMembershipSourceCustomLabel', () => {
  it('pending sets saving', () => {
    const state = sqlReducer(initial, patchDefaultSqlMembershipSourceCustomLabel.pending('r', {} as any));
    expect(state.isSourceSaving).toBe(true);
  });
  it('fulfilled clears saving', () => {
    const state = sqlReducer(initial, patchDefaultSqlMembershipSourceCustomLabel.fulfilled({} as any, 'r', {} as any));
    expect(state.isSourceSaving).toBe(false);
    expect(state.patchError).toBeUndefined();
  });
  it('rejected sets error', () => {
    const state = sqlReducer(initial, patchDefaultSqlMembershipSourceCustomLabel.rejected(new Error('pe'), 'r', {} as any));
    expect(state.isSourceSaving).toBe(false);
    expect(state.patchError).toBe('pe');
  });
});

describe('sqlMembershipSources.slice — patchDefaultSqlMembershipSourceAttributes', () => {
  it('pending sets saving', () => {
    const state = sqlReducer(initial, patchDefaultSqlMembershipSourceAttributes.pending('r', {} as any));
    expect(state.areAttributesSaving).toBe(true);
  });
  it('fulfilled clears saving', () => {
    const state = sqlReducer(initial, patchDefaultSqlMembershipSourceAttributes.fulfilled({} as any, 'r', {} as any));
    expect(state.areAttributesSaving).toBe(false);
    expect(state.patchError).toBeUndefined();
  });
  it('rejected sets error', () => {
    const state = sqlReducer(initial, patchDefaultSqlMembershipSourceAttributes.rejected(new Error('ae'), 'r', {} as any));
    expect(state.areAttributesSaving).toBe(false);
    expect(state.patchError).toBe('ae');
  });
});

describe('sqlMembershipSources.slice — selectors', () => {
  const root = { sqlMembershipSources: { ...initial, source: { name: 's' }, attributes: [{ name: 'a' }], attributeMappings: { x: { mappings: [], type: 't' } }, isSourceLoading: true, areAttributesLoading: true, areAttributeMappingsLoading: true, isSourceSaving: true, areAttributesSaving: true } } as any;

  it('selectSource', () => expect(selectSource(root)).toEqual({ name: 's' }));
  it('selectAttributes', () => expect(selectAttributes(root)).toEqual([{ name: 'a' }]));
  it('selectAttributeMappings', () => expect(selectAttributeMappings(root)).toEqual({ x: { mappings: [], type: 't' } }));
  it('selectIsSourceLoading', () => expect(selectIsSourceLoading(root)).toBe(true));
  it('selectAreAttributesLoading', () => expect(selectAreAttributesLoading(root)).toBe(true));
  it('selectAreAttributeMappingsLoading', () => expect(selectAreAttributeMappingsLoading(root)).toBe(true));
  it('selectIsSourceSaving', () => expect(selectIsSourceSaving(root)).toBe(true));
  it('selectAreAttributesSaving', () => expect(selectAreAttributesSaving(root)).toBe(true));
});
