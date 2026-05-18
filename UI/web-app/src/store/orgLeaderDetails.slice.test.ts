// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import orgLeaderDetailsReducer, {
  updateOrgLeaderDetails,
  selectOrgLeaderDetails,
  selectObjectIdEmployeeIdMapping,
  selectOrgLeaderDataReturned,
} from './orgLeaderDetails.slice';
import { fetchOrgLeaderDetails } from './orgLeaderDetails.api';

const initial = orgLeaderDetailsReducer(undefined, { type: '@@INIT' });

describe('orgLeaderDetails.slice — reducers', () => {
  it('updateOrgLeaderDetails sets employeeId', () => {
    const state = orgLeaderDetailsReducer(initial, updateOrgLeaderDetails({ employeeId: 42 }));
    expect(state.employeeId).toBe(42);
  });
});

describe('orgLeaderDetails.slice — extraReducers', () => {
  it('pending sets orgLeaderDataReturned to false', () => {
    const state = orgLeaderDetailsReducer(initial, fetchOrgLeaderDetails.pending('req1', {} as any));
    expect(state.orgLeaderDataReturned).toBe(false);
  });

  it('fulfilled sets full state', () => {
    const payload = {
      employeeId: 100,
      objectId: 'obj-1',
      text: 'John',
      maxDepth: 5,
      partId: 'part-1',
    };
    const state = orgLeaderDetailsReducer(initial, fetchOrgLeaderDetails.fulfilled(payload as any, 'req1', {} as any));
    expect(state.employeeId).toBe(100);
    expect(state.objectId).toBe('obj-1');
    expect(state.text).toBe('John');
    expect(state.maxDepth).toBe(5);
    expect(state.partId).toBe('part-1');
    expect(state.orgLeaderDataReturned).toBe(true);
    expect(state.mapping[100]).toEqual({ objectId: 'obj-1', text: 'John', maxDepth: 5 });
  });

  it('fulfilled accumulates mapping entries', () => {
    const p1 = { employeeId: 1, objectId: 'o1', text: 'A', maxDepth: 2, partId: 'p1' };
    const p2 = { employeeId: 2, objectId: 'o2', text: 'B', maxDepth: 3, partId: 'p2' };
    let state = orgLeaderDetailsReducer(initial, fetchOrgLeaderDetails.fulfilled(p1 as any, 'r1', {} as any));
    state = orgLeaderDetailsReducer(state, fetchOrgLeaderDetails.fulfilled(p2 as any, 'r2', {} as any));
    expect(state.mapping[1]).toBeDefined();
    expect(state.mapping[2]).toBeDefined();
  });

  it('rejected sets orgLeaderDataReturned to false', () => {
    const state = orgLeaderDetailsReducer(initial, fetchOrgLeaderDetails.rejected(new Error('e'), 'req1', {} as any));
    expect(state.orgLeaderDataReturned).toBe(false);
  });
});

describe('orgLeaderDetails.slice — selectors', () => {
  const root = { orgLeaderDetails: { ...initial, mapping: { 1: { objectId: 'o', text: 't', maxDepth: 1 } }, orgLeaderDataReturned: true } } as any;

  it('selectOrgLeaderDetails returns state', () => {
    expect(selectOrgLeaderDetails(root)).toBe(root.orgLeaderDetails);
  });

  it('selectObjectIdEmployeeIdMapping returns mapping', () => {
    expect(selectObjectIdEmployeeIdMapping(root)).toEqual({ 1: { objectId: 'o', text: 't', maxDepth: 1 } });
  });

  it('selectOrgLeaderDataReturned returns flag', () => {
    expect(selectOrgLeaderDataReturned(root)).toBe(true);
  });
});
