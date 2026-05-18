// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import sqlReducer, {
  setSource,
  setAttributes,
  setAttributeMappings,
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
    const state = sqlReducer(initial, setAttributeMappings({ attribute: 'dept', type: 'string', mappings: [{ value: 'eng' }] }));
    expect(state.attributeMappings['dept']).toEqual({ mappings: [{ value: 'eng' }], type: 'string' });
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
    const payload = { attribute: 'dept', type: 'string', mappings: [{ v: 1 }] };
    const state = sqlReducer(initial, fetchAttributeMappings.fulfilled(payload as any, 'r', {} as any));
    expect(state.areAttributeMappingsLoading).toBe(false);
    expect(state.attributeMappings['dept']).toEqual({ mappings: [{ v: 1 }], type: 'string' });
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
