// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { JobDetails } from './JobDetails';

// Declared via vi.hoisted so testJob is initialized before the hoisted vi.mock factories run.
const { testJob } = vi.hoisted(() => ({
  testJob: {
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
}));

// Simulate the UA bug repro: a group deeplink (/Groups/:groupId) with only groupId, no jobId route param.
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual<typeof import('react-router-dom')>('react-router-dom');
  return {
    ...actual,
    useNavigate: () => vi.fn(),
    useParams: () => ({ groupId: 'test-group-id' }),
  };
});

// Replace getGroupDetails with a network-free thunk so selectedJob populates deterministically (pending still clears it, as in production).
vi.mock('../../store/jobDetails.api', async () => {
  const actual = await vi.importActual<typeof import('../../store/jobDetails.api')>('../../store/jobDetails.api');
  const { createAsyncThunk } = await vi.importActual<typeof import('@reduxjs/toolkit')>('@reduxjs/toolkit');
  return {
    ...actual,
    getGroupDetails: createAsyncThunk('groupDetailsTestMock', async () => testJob),
  };
});

// Mock the Run History panel to observe its mounted state/props without its data-fetching / SignalR deps.
vi.mock('../../components/JobHistoryPanel/JobHistoryPanel', () => ({
  JobHistoryPanel: (props: { isOpen: boolean; jobId: string }) => (
    <div
      data-testid="job-history-panel"
      data-open={String(props.isOpen)}
      data-jobid={props.jobId}
    />
  ),
}));

const mockAuthService = {
  getTokenAsync: vi.fn().mockResolvedValue('mock-token'),
};

const createState = () => ({
  roles: {
    isSubmissionReviewer: false,
    isJobOwnerEnabler: false,
    isJobOwnerDeleter: false,
    isJobOwnerReader: true,
    isJobOwnerWriter: false,
    isJobTenantReader: false,
    isJobTenantWriter: false,
    isAutoApproverAdministrator: false,
    isCustomMembershipProviderAdministrator: false,
    isOperationsResetAdministrator: false,
    isGeneralSettingsAdministrator: false,
    isFetchingRoles: false,
  },
  jobs: {
    selectedJob: undefined,
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

function renderJobDetails() {
  // Keep other on-mount thunks (job changes, profile photos) benign to avoid unhandled rejections.
  vi.stubGlobal(
    'fetch',
    vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      json: () => Promise.resolve({}),
      text: () => Promise.resolve(''),
    }) as unknown as typeof fetch
  );

  return renderWithProviders(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <JobDetails />
    </MemoryRouter>,
    {
      preloadedState: createState() as any,
      serviceMocks: { authenticationService: mockAuthService as any },
    }
  );
}

describe('JobDetails - Run History from a group deeplink', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('renders the Run History panel with the loaded job syncJobId when there is no jobId route param', async () => {
    renderJobDetails();

    await screen.findAllByText('Test Group');

    const panel = screen.getByTestId('job-history-panel');
    expect(panel).toBeInTheDocument();
    // Falls back to the loaded job's syncJobId since the route only carries groupId.
    expect(panel).toHaveAttribute('data-jobid', 'test-sync-job-id');
    expect(panel).toHaveAttribute('data-open', 'false');
  });

  it('opens the Run History panel when the History button is clicked from a group deeplink', async () => {
    renderJobDetails();

    await screen.findAllByText('Test Group');

    const historyButton = screen.getByRole('button', { name: /^history$/i });
    fireEvent.click(historyButton);

    await waitFor(() => {
      expect(screen.getByTestId('job-history-panel')).toHaveAttribute('data-open', 'true');
    });
  });
});
