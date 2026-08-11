// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { waitFor } from '@testing-library/react';
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { JobDetails } from './JobDetails';
import manageMembershipReducer, { setIsAdvancedViewReadOnly } from '../../store/manageMembership.slice';
import rolesReducer from '../../store/roles.slice';
import type { RootState } from '../../store';

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useNavigate: () => vi.fn(),
    useParams: () => ({ jobId: 'test-sync-job-id' }),
  };
});

const mockAuthService = {
  getTokenAsync: vi.fn().mockResolvedValue('mock-token'),
};

const getManageMembershipState = (): RootState['manageMembership'] =>
  manageMembershipReducer(undefined, { type: 'test/init' });

const getRolesState = (): RootState['roles'] =>
  rolesReducer(undefined, { type: 'test/init' });

const buildState = () => ({
  roles: { ...getRolesState(), isJobTenantReader: true },
  // Simulate advanced view left on from a previously viewed job.
  manageMembership: {
    ...getManageMembershipState(),
    isAdvancedView: true,
    advancedViewQuery: '[{"type":"GroupMembership","source":"stale-group"}]',
  },
  jobs: {
    selectedJob: {
      syncJobId: 'test-sync-job-id',
      targetGroupId: 'test-group-id',
      targetChannelId: '',
      targetDestinationType: 'SecurityGroup',
      targetGroupName: 'Test Group',
      targetChannelName: '',
      email: '',
      startDate: '2024-01-01T00:00:00Z',
      lastSuccessfulStartTime: '',
      lastSuccessfulRunTime: '',
      query: '',
      titles: [],
      actionRequired: '',
      enabledOrNot: true,
      status: 'Idle',
      period: 24,
      arrow: '',
      estimatedNextRunTime: '',
      endpoints: [],
      requestor: '',
      thresholdPercentageForAdditions: 100,
      thresholdPercentageForRemovals: 20,
    },
    selectedJobLoading: false,
    getJobsError: undefined,
    getJobDetailsError: undefined,
    patchJobDetailsResponse: undefined,
    patchJobDetailsError: undefined,
    jobsLoading: false,
    jobs: undefined,
    jobsToDownload: undefined,
    downloadJobsLoading: false,
    downloadJobsError: undefined,
    postJobLoading: false,
    postJobError: undefined,
    removeGMMLoading: false,
    removeGMMResponse: undefined,
    removeGMMError: undefined,
    approveJobsLoading: false,
    totalNumberOfApprovedJobs: undefined,
    totalNumberOfJobs: undefined,
    approveJobsError: undefined,
    selectedJobWithNoTitles: false,
    generatedTitlesYet: false,
    jobIdSet: '',
    jobOwnerFilterSuggestions: [],
    selectedJobChanges: undefined,
    selectedJobChangesLoading: false,
    selectedJobChangesError: undefined,
  },
});

describe('JobDetails - advanced view state', () => {
  beforeEach(() => {
    global.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: () => Promise.resolve({}),
      text: () => Promise.resolve(''),
    }) as unknown as typeof global.fetch;
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('resets the shared advanced view flag when the page loads', async () => {
    const { store } = renderWithProviders(
      <MemoryRouter>
        <JobDetails />
      </MemoryRouter>,
      {
        preloadedState: buildState() as any,
        serviceMocks: { authenticationService: mockAuthService as any },
      }
    );

    await waitFor(() =>
      expect(store.getState().manageMembership.isAdvancedView).toBe(false)
    );
  });

  it('resets the shared advanced view flag when leaving the page', async () => {
    const { store, unmount } = renderWithProviders(
      <MemoryRouter>
        <JobDetails />
      </MemoryRouter>,
      {
        preloadedState: buildState() as any,
        serviceMocks: { authenticationService: mockAuthService as any },
      }
    );

    await waitFor(() =>
      expect(store.getState().manageMembership.isAdvancedView).toBe(false)
    );

    // Simulate a reader turning the read-only advanced view back on before navigating away.
    store.dispatch(setIsAdvancedViewReadOnly(true));
    expect(store.getState().manageMembership.isAdvancedView).toBe(true);

    unmount();

    expect(store.getState().manageMembership.isAdvancedView).toBe(false);
  });
});
