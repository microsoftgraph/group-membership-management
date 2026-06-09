// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';

import { fetchJobChanges, fetchJobDetails, getChannelDetails, getGroupDetails } from './jobDetails.api';
import { fetchJobs } from './jobs.api';
import jobsReducer from './jobs.slice';
import type { Job } from '../models/Job';
import type { SyncJobChange } from '../models';

// Minimal Job factory; only fields the reducer reads (titles for
// selectedJobWithNoTitles + an identifier so test assertions are obvious).
const makeJob = (id: string, overrides: Partial<Job> = {}): Job =>
  ({
    syncJobId: id,
    targetGroupId: id,
    titles: [],
    ...overrides,
  } as unknown as Job);

const initial = jobsReducer(undefined, { type: '@@INIT' });

describe('jobs.slice — selectedJob requestId guard (Bug 2 race)', () => {
  it('discards a fetchJobDetails response whose requestId no longer matches the most recent pending', () => {
    // Pending A
    let state = jobsReducer(
      initial,
      fetchJobDetails.pending('reqA', { syncJobId: 'A' })
    );
    expect(state.selectedJobRequestId).toBe('reqA');

    // Pending B (user navigated to a different job before A returned)
    state = jobsReducer(state, fetchJobDetails.pending('reqB', { syncJobId: 'B' }));
    expect(state.selectedJobRequestId).toBe('reqB');
    expect(state.selectedJob).toBeUndefined();

    // A's response arrives late — must be ignored
    state = jobsReducer(
      state,
      fetchJobDetails.fulfilled(makeJob('A'), 'reqA', { syncJobId: 'A' })
    );
    expect(state.selectedJob).toBeUndefined();
    expect(state.selectedJobLoading).toBe(true);

    // B's response arrives — applied
    state = jobsReducer(
      state,
      fetchJobDetails.fulfilled(makeJob('B'), 'reqB', { syncJobId: 'B' })
    );
    expect(state.selectedJob?.syncJobId).toBe('B');
    expect(state.selectedJobLoading).toBe(false);
  });

  it('shares the guard across fetchJobDetails / getGroupDetails / getChannelDetails (cross-thunk race)', () => {
    // Critical: all three thunks write the same selectedJob slot, so a
    // late fetchJobDetails response cannot clobber a newer getGroupDetails
    // response for a different target.
    let state = jobsReducer(
      initial,
      fetchJobDetails.pending('reqA', { syncJobId: 'A' })
    );
    state = jobsReducer(state, getGroupDetails.pending('reqB', 'group-B'));

    // fetchJobDetails(A) fulfills late — guard must drop it
    state = jobsReducer(
      state,
      fetchJobDetails.fulfilled(makeJob('A'), 'reqA', { syncJobId: 'A' })
    );
    expect(state.selectedJob).toBeUndefined();

    // getGroupDetails(B) fulfills — applied
    state = jobsReducer(
      state,
      getGroupDetails.fulfilled(makeJob('group-B'), 'reqB', 'group-B')
    );
    expect(state.selectedJob?.syncJobId).toBe('group-B');
  });

  it('ignores a getChannelDetails response after a newer fetchJobDetails has been dispatched', () => {
    let state = jobsReducer(
      initial,
      getChannelDetails.pending('reqChan', { groupId: 'G', channelId: 'C' })
    );
    state = jobsReducer(state, fetchJobDetails.pending('reqJob', { syncJobId: 'J' }));

    state = jobsReducer(
      state,
      getChannelDetails.fulfilled(makeJob('chan-stale'), 'reqChan', { groupId: 'G', channelId: 'C' })
    );
    expect(state.selectedJob).toBeUndefined();

    state = jobsReducer(
      state,
      fetchJobDetails.fulfilled(makeJob('J'), 'reqJob', { syncJobId: 'J' })
    );
    expect(state.selectedJob?.syncJobId).toBe('J');
  });

  it('drops a stale rejected action so it cannot overwrite a newer pending state', () => {
    let state = jobsReducer(
      initial,
      fetchJobDetails.pending('reqA', { syncJobId: 'A' })
    );
    state = jobsReducer(state, fetchJobDetails.pending('reqB', { syncJobId: 'B' }));

    // A rejects late
    state = jobsReducer(
      state,
      fetchJobDetails.rejected(new Error('A timed out'), 'reqA', { syncJobId: 'A' })
    );
    // Loading must remain true (B is still in flight) and no error is surfaced
    expect(state.selectedJobLoading).toBe(true);
    expect(state.getJobDetailsError).toBeUndefined();
  });
});

describe('jobs.slice — selectedJobChanges requestId guard', () => {
  it('discards a stale fetchJobChanges response from a previous job', () => {
    let state = jobsReducer(initial, fetchJobChanges.pending('reqA', { syncJobId: 'A' }));
    state = jobsReducer(state, fetchJobChanges.pending('reqB', { syncJobId: 'B' }));

    const stalePayload = [{ id: 'A1' } as unknown as SyncJobChange];
    state = jobsReducer(
      state,
      fetchJobChanges.fulfilled(stalePayload, 'reqA', { syncJobId: 'A' })
    );
    expect(state.selectedJobChanges).toBeUndefined();
    expect(state.selectedJobChangesLoading).toBe(true);

    const freshPayload = [{ id: 'B1' } as unknown as SyncJobChange];
    state = jobsReducer(
      state,
      fetchJobChanges.fulfilled(freshPayload, 'reqB', { syncJobId: 'B' })
    );
    expect(state.selectedJobChanges).toBe(freshPayload);
    expect(state.selectedJobChangesLoading).toBe(false);
  });

  it('clears selectedJobChangesLoading on rejected (pre-existing bug fix)', () => {
    let state = jobsReducer(initial, fetchJobChanges.pending('reqA', { syncJobId: 'A' }));
    state = jobsReducer(
      state,
      fetchJobChanges.rejected(new Error('boom'), 'reqA', { syncJobId: 'A' })
    );
    expect(state.selectedJobChangesLoading).toBe(false);
    expect(state.selectedJobChangesError).toBeDefined();
  });
});

describe('jobs.slice — fetchJobs.pending preserves cached list (matches prod UX)', () => {
  it('keeps state.jobs visible during refresh so cached items don\'t flash to shimmer', () => {
    const seeded = jobsReducer(initial, {
      type: 'jobs/setJobs',
      payload: [makeJob('stale-1'), makeJob('stale-2')],
    });
    expect(seeded.jobs).toHaveLength(2);

    const refreshing = jobsReducer(seeded, fetchJobs.pending('req1', undefined as never));
    expect(refreshing.jobs).toHaveLength(2);
    expect(refreshing.jobsLoading).toBe(true);
  });
});

describe('jobs.slice — removeJobFromList (optimistic remove for Bug 1)', () => {
  it('removes the matching job by syncJobId', () => {
    const seeded = jobsReducer(initial, {
      type: 'jobs/setJobs',
      payload: [makeJob('a'), makeJob('b'), makeJob('c')],
    });
    const next = jobsReducer(seeded, { type: 'jobs/removeJobFromList', payload: 'b' });
    expect(next.jobs?.map(j => j.syncJobId)).toEqual(['a', 'c']);
  });

  it('is a no-op when state.jobs is undefined', () => {
    const next = jobsReducer(initial, { type: 'jobs/removeJobFromList', payload: 'a' });
    expect(next.jobs).toBeUndefined();
  });

  it('is a no-op when the id is not in the list', () => {
    const seeded = jobsReducer(initial, {
      type: 'jobs/setJobs',
      payload: [makeJob('a'), makeJob('b')],
    });
    const next = jobsReducer(seeded, { type: 'jobs/removeJobFromList', payload: 'missing' });
    expect(next.jobs?.map(j => j.syncJobId)).toEqual(['a', 'b']);
  });
});
