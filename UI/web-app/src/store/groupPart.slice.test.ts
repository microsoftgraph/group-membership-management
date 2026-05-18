// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import groupPartReducer, { selectSelectedGroupById } from './groupPart.slice';
import { searchDestinations } from './manageMembership.api';

const initial = groupPartReducer(undefined, { type: '@@INIT' });

describe('groupPart.slice — extraReducers', () => {
  it('searchDestinations.fulfilled sets searchResults', () => {
    const results = [{ id: 'g1', name: 'Group 1' }, { id: 'g2', name: 'Group 2' }];
    const state = groupPartReducer(initial, searchDestinations.fulfilled(results as any, 'r1', '' as any));
    expect(state.searchResults).toEqual(results);
  });
});

describe('groupPart.slice — selectors', () => {
  it('selectSelectedGroupById finds a group', () => {
    const root = { groupPart: { searchResults: [{ id: 'g1', name: 'A' }, { id: 'g2', name: 'B' }] } } as any;
    expect(selectSelectedGroupById(root, 'g2')).toEqual({ id: 'g2', name: 'B' });
  });

  it('selectSelectedGroupById returns undefined for missing id', () => {
    const root = { groupPart: { searchResults: [{ id: 'g1', name: 'A' }] } } as any;
    expect(selectSelectedGroupById(root, 'missing')).toBeUndefined();
  });

  it('selectSelectedGroupById returns undefined when searchResults is undefined', () => {
    const root = { groupPart: { searchResults: undefined } } as any;
    expect(selectSelectedGroupById(root, 'g1')).toBeUndefined();
  });
});
