// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';

import { fetchJobChanges, fetchJobDetails, getChannelDetails, getGroupDetails, patchJobDetails, removeGMM } from './jobDetails.api';
import { fetchJobs, postJob, downloadJobs, approveJobs, getPeoplePickerSuggestions } from './jobs.api';
import jobsReducer from './jobs.slice';
import type { Job } from '../models/Job';
import type { SyncJobChange, PatchJobResponse, RemoveGMMResponse } from '../models';
import type { PeoplePickerPersona } from '../models/PeoplePickerPersona';

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

describe('jobs.slice — setSelectedJobEnabled', () => {
  it('updates enabledOrNot on selectedJob', () => {
    let state = jobsReducer(initial, fetchJobDetails.pending('req1', { syncJobId: 'a' }));
    state = jobsReducer(state, fetchJobDetails.fulfilled(makeJob('a', { enabledOrNot: true } as any), 'req1', { syncJobId: 'a' }));
    expect((state.selectedJob as any)?.enabledOrNot).toBe(true);

    state = jobsReducer(state, { type: 'jobs/setSelectedJobEnabled', payload: false });
    expect((state.selectedJob as any)?.enabledOrNot).toBe(false);
  });

  it('is a no-op when selectedJob is undefined', () => {
    const state = jobsReducer(initial, { type: 'jobs/setSelectedJobEnabled', payload: true });
    expect(state.selectedJob).toBeUndefined();
  });
});

describe('jobs.slice — setSelectedJobStatus', () => {
  it('updates status on selectedJob', () => {
    let state = jobsReducer(initial, fetchJobDetails.pending('req1', { syncJobId: 'a' }));
    state = jobsReducer(state, fetchJobDetails.fulfilled(makeJob('a'), 'req1', { syncJobId: 'a' }));

    state = jobsReducer(state, { type: 'jobs/setSelectedJobStatus', payload: 'CustomerPaused' });
    expect(state.selectedJob?.status).toBe('CustomerPaused');
  });

  it('is a no-op when selectedJob is undefined', () => {
    const state = jobsReducer(initial, { type: 'jobs/setSelectedJobStatus', payload: 'Idle' });
    expect(state.selectedJob).toBeUndefined();
  });
});

describe('jobs.slice — setTitles', () => {
  it('sets titles and selectedJobWithNoTitles to false when titles have names', () => {
    let state = jobsReducer(initial, fetchJobDetails.pending('req1', { syncJobId: 'a' }));
    state = jobsReducer(state, fetchJobDetails.fulfilled(makeJob('a'), 'req1', { syncJobId: 'a' }));

    state = jobsReducer(state, { type: 'jobs/setTitles', payload: [{ name: 'Title 1' }] });
    expect(state.selectedJob?.titles).toHaveLength(1);
    expect(state.selectedJobWithNoTitles).toBe(false);
  });

  it('sets selectedJobWithNoTitles to true when all titles are empty', () => {
    let state = jobsReducer(initial, fetchJobDetails.pending('req1', { syncJobId: 'a' }));
    state = jobsReducer(state, fetchJobDetails.fulfilled(makeJob('a'), 'req1', { syncJobId: 'a' }));

    state = jobsReducer(state, { type: 'jobs/setTitles', payload: [{ name: '' }, { name: '  ' }] });
    expect(state.selectedJobWithNoTitles).toBe(true);
  });
});

describe('jobs.slice — patchJobDetails extraReducers', () => {
  it('clears response and error on pending', () => {
    const state = jobsReducer(initial, patchJobDetails.pending('req1', {} as any));
    expect(state.patchJobDetailsResponse).toBeUndefined();
    expect(state.patchJobDetailsError).toBeUndefined();
  });

  it('sets response and clears jobIdSet on fulfilled', () => {
    const seeded = jobsReducer(initial, { type: 'jobs/setJobId', payload: 'some-id' });
    const response = { ok: true, statusCode: 200 } as PatchJobResponse;
    const state = jobsReducer(seeded, patchJobDetails.fulfilled(response, 'req1', {} as any));
    expect(state.patchJobDetailsResponse).toEqual(response);
    expect(state.jobIdSet).toBe('');
  });

  it('sets error on rejected', () => {
    const state = jobsReducer(initial, patchJobDetails.rejected(new Error('patch fail'), 'req1', {} as any));
    expect(state.patchJobDetailsError).toBe('patch fail');
  });
});

describe('jobs.slice — postJob extraReducers', () => {
  it('sets loading on pending', () => {
    const state = jobsReducer(initial, postJob.pending('req1', {} as any));
    expect(state.postJobLoading).toBe(true);
    expect(state.postJobError).toBeUndefined();
  });

  it('clears loading on fulfilled', () => {
    const state = jobsReducer(initial, postJob.fulfilled(undefined as any, 'req1', {} as any));
    expect(state.postJobLoading).toBe(false);
  });

  it('sets error on rejected', () => {
    const state = jobsReducer(initial, postJob.rejected(new Error('post fail'), 'req1', {} as any));
    expect(state.postJobLoading).toBe(false);
    expect(state.postJobError).toBe('post fail');
  });
});

describe('jobs.slice — removeGMM extraReducers', () => {
  it('sets loading on pending', () => {
    const state = jobsReducer(initial, removeGMM.pending('req1', { syncJobId: 'a' }));
    expect(state.removeGMMLoading).toBe(true);
    expect(state.removeGMMResponse).toBeUndefined();
    expect(state.removeGMMError).toBeUndefined();
  });

  it('sets response on fulfilled', () => {
    const response = { ok: true, statusCode: 200 } as RemoveGMMResponse;
    const state = jobsReducer(initial, removeGMM.fulfilled(response, 'req1', { syncJobId: 'a' }));
    expect(state.removeGMMLoading).toBe(false);
    expect(state.removeGMMResponse).toEqual(response);
  });

  it('sets error on rejected', () => {
    const state = jobsReducer(initial, removeGMM.rejected(new Error('remove fail'), 'req1', { syncJobId: 'a' }));
    expect(state.removeGMMLoading).toBe(false);
    expect(state.removeGMMError).toBe('remove fail');
  });
});

describe('jobs.slice — downloadJobs extraReducers', () => {
  it('sets loading on pending', () => {
    const state = jobsReducer(initial, downloadJobs.pending('req1', undefined as any));
    expect(state.downloadJobsLoading).toBe(true);
  });

  it('sets jobsToDownload on fulfilled', () => {
    const jobs = [makeJob('d1')];
    const state = jobsReducer(initial, downloadJobs.fulfilled(jobs, 'req1', undefined as any));
    expect(state.downloadJobsLoading).toBe(false);
    expect(state.jobsToDownload).toEqual(jobs);
  });

  it('sets error on rejected', () => {
    const state = jobsReducer(initial, downloadJobs.rejected(new Error('dl fail'), 'req1', undefined as any));
    expect(state.downloadJobsLoading).toBe(false);
    expect(state.downloadJobsError).toBe('dl fail');
  });
});

describe('jobs.slice — approveJobs extraReducers', () => {
  it('sets loading on pending', () => {
    const state = jobsReducer(initial, approveJobs.pending('req1', undefined as any));
    expect(state.approveJobsLoading).toBe(true);
  });

  it('sets totals on fulfilled', () => {
    const payload = { totalNumberOfApprovedJobs: 5, totalNumberOfJobs: 10 };
    const state = jobsReducer(initial, approveJobs.fulfilled(payload as any, 'req1', undefined as any));
    expect(state.approveJobsLoading).toBe(false);
    expect(state.totalNumberOfApprovedJobs).toBe(5);
    expect(state.totalNumberOfJobs).toBe(10);
  });

  it('sets error on rejected', () => {
    const state = jobsReducer(initial, approveJobs.rejected(new Error('approve fail'), 'req1', undefined as any));
    expect(state.approveJobsLoading).toBe(false);
    expect(state.approveJobsError).toBe('approve fail');
  });
});

describe('jobs.slice — simple reducers', () => {
  it('setGetJobsError clears error', () => {
    const seeded = jobsReducer({ ...initial, getJobsError: 'some error' } as any, { type: '@@SEED' });
    // Direct approach: use the actual initial with error set
    let state = jobsReducer(initial, fetchJobs.rejected(new Error('err'), 'req1', undefined as never));
    state = jobsReducer(state, { type: 'jobs/setGetJobsError' });
    expect(state.getJobsError).toBeUndefined();
  });

  it('setGetJobDetailsError clears error', () => {
    let state = jobsReducer(initial, fetchJobDetails.pending('req1', { syncJobId: 'a' }));
    state = jobsReducer(state, fetchJobDetails.rejected(new Error('err'), 'req1', { syncJobId: 'a' }));
    state = jobsReducer(state, { type: 'jobs/setGetJobDetailsError' });
    expect(state.getJobDetailsError).toBeUndefined();
  });

  it('clearJob clears selectedJob', () => {
    let state = jobsReducer(initial, fetchJobDetails.pending('req1', { syncJobId: 'a' }));
    state = jobsReducer(state, fetchJobDetails.fulfilled(makeJob('a'), 'req1', { syncJobId: 'a' }));
    state = jobsReducer(state, { type: 'jobs/clearJob' });
    expect(state.selectedJob).toBeUndefined();
  });

  it('clearJobsToDownload clears jobsToDownload', () => {
    const jobs = [makeJob('a')];
    let state = jobsReducer(initial, downloadJobs.fulfilled(jobs, 'req1', undefined as any));
    state = jobsReducer(state, { type: 'jobs/clearJobsToDownload' });
    expect(state.jobsToDownload).toBeUndefined();
  });

  it('updateJobOwnerFilterSuggestions clears suggestions', () => {
    const state = jobsReducer(initial, { type: 'jobs/updateJobOwnerFilterSuggestions' });
    expect(state.jobOwnerFilterSuggestions).toEqual([]);
  });

  it('setGeneratedTitlesYet updates flag', () => {
    const state = jobsReducer(initial, { type: 'jobs/setGeneratedTitlesYet', payload: true });
    expect(state.generatedTitlesYet).toBe(true);
  });

  it('setJobId updates jobIdSet', () => {
    const state = jobsReducer(initial, { type: 'jobs/setJobId', payload: 'job-123' });
    expect(state.jobIdSet).toBe('job-123');
  });

  it('setApproveJobsLoading clears loading', () => {
    let state = jobsReducer(initial, approveJobs.pending('req1', undefined as any));
    state = jobsReducer(state, { type: 'jobs/setApproveJobsLoading' });
    expect(state.approveJobsLoading).toBe(false);
  });

  it('setApproveJobsResponse clears totals', () => {
    const payload = { totalNumberOfApprovedJobs: 5, totalNumberOfJobs: 10 };
    let state = jobsReducer(initial, approveJobs.fulfilled(payload as any, 'req1', undefined as any));
    state = jobsReducer(state, { type: 'jobs/setApproveJobsResponse' });
    expect(state.totalNumberOfApprovedJobs).toBeUndefined();
    expect(state.totalNumberOfJobs).toBeUndefined();
  });
});
