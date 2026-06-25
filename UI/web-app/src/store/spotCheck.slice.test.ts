// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';

import spotCheckReducer, { clearSpotCheck } from './spotCheck.slice';
import { spotCheckUser } from './spotCheck.api';
import { SpotCheckResult } from '../models/SpotCheckResult';

const initial = spotCheckReducer(undefined, { type: '@@INIT' });

const sampleResult: SpotCheckResult = {
  accountEnabled: true,
  hasUnsupportedParts: false,
  parts: [
    { index: 0, type: 'GroupMembership', supported: true, exclusionary: false, included: true },
  ],
};

describe('spotCheck.slice', () => {
  it('has the expected initial state', () => {
    expect(initial.status).toBe('idle');
    expect(initial.result).toBeUndefined();
    expect(initial.error).toBeUndefined();
  });

  it('sets loading and clears previous result/error on pending', () => {
    const prior = { status: 'succeeded' as const, result: sampleResult, error: 'old' };
    const state = spotCheckReducer(
      prior,
      spotCheckUser.pending('req', { syncJobId: 'job', userId: 'user' })
    );
    expect(state.status).toBe('loading');
    expect(state.result).toBeUndefined();
    expect(state.error).toBeUndefined();
  });

  it('stores the result on fulfilled', () => {
    const state = spotCheckReducer(
      initial,
      spotCheckUser.fulfilled(sampleResult, 'req', { syncJobId: 'job', userId: 'user' })
    );
    expect(state.status).toBe('succeeded');
    expect(state.result).toEqual(sampleResult);
    expect(state.error).toBeUndefined();
  });

  it('stores the error message on rejected', () => {
    const action = spotCheckUser.rejected(
      new Error('notFound'),
      'req',
      { syncJobId: 'job', userId: 'user' }
    );
    const state = spotCheckReducer(initial, action);
    expect(state.status).toBe('failed');
    expect(state.result).toBeUndefined();
    expect(state.error).toBe('notFound');
  });

  it('resets state on clearSpotCheck', () => {
    const prior = { status: 'succeeded' as const, result: sampleResult, error: undefined };
    const state = spotCheckReducer(prior, clearSpotCheck());
    expect(state.status).toBe('idle');
    expect(state.result).toBeUndefined();
    expect(state.error).toBeUndefined();
  });
});
