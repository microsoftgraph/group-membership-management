// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import ownerReducer, { setOwner, selectOwner } from './owner.slice';
import { addOwner } from './owner.api';

const initial = ownerReducer(undefined, { type: '@@INIT' });

describe('owner.slice — reducers', () => {
  it('setOwner sets status', () => {
    const state = ownerReducer(initial, setOwner('Added'));
    expect(state.status).toBe('Added');
  });
});

describe('owner.slice — extraReducers', () => {
  it('addOwner.fulfilled sets status and clears loading', () => {
    const seeded = { ...initial, loading: true };
    const state = ownerReducer(seeded, addOwner.fulfilled('Success', 'r1', {} as any));
    expect(state.loading).toBe(false);
    expect(state.status).toBe('Success');
  });
});

describe('owner.slice — selectors', () => {
  it('selectOwner returns owner state', () => {
    const root = { owner: { loading: false, status: 'ok' } } as any;
    expect(selectOwner(root)).toEqual({ loading: false, status: 'ok' });
  });
});
