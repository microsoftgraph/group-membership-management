// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { JobDetails } from './JobDetails';

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

const createBaseState = (overrides: {
  isSubmissionReviewer?: boolean;
  jobStatus?: string;
  jobEnabled?: boolean;
  targetGroupName?: string;
} = {}) => ({
  roles: {
    isSubmissionReviewer: overrides.isSubmissionReviewer ?? true,
    isSubmissionRejector: false,
    isJobOwnerEnabler: false,
    isJobOwnerDeleter: false,
    isJobOwnerReader: false,
    isJobOwnerWriter: false,
    isJobTenantReader: false,
    isJobTenantWriter: false,
    isHyperlinkAdministrator: false,
    isCustomMembershipProviderAdministrator: false,
    isOperationsResetAdministrator: false,
    isGeneralSettingsAdministrator: false,
    isFetchingRoles: false,
  },
  jobs: {
    selectedJob: {
      syncJobId: 'test-sync-job-id',
      targetGroupId: 'test-group-id',
      targetChannelId: '',
      targetDestinationType: 'SecurityGroup',
      targetGroupName: overrides.targetGroupName ?? 'Test Group',
      targetChannelName: '',
      email: '',
      startDate: '2024-01-01T00:00:00Z',
      lastSuccessfulStartTime: '',
      lastSuccessfulRunTime: '',
      query: '',
      titles: [],
      actionRequired: '',
      enabledOrNot: overrides.jobEnabled ?? true,
      status: overrides.jobStatus ?? 'Idle',
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

const defaultJob = {
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
};

function createFetchMock(
  usage: { count: number; limit: number; remaining: number } | null,
  jobOverrides?: Partial<typeof defaultJob>
) {
  const job = { ...defaultJob, ...jobOverrides };
  return vi.fn().mockImplementation((url: string, _options?: RequestInit) => {
    const urlStr = String(url);

    // fetchJobDetails - URL contains /jobDetails/job/
    if (urlStr.includes('/jobDetails/job/') || urlStr.includes('/job/')) {
      return Promise.resolve({
        ok: true,
        status: 200,
        json: () => Promise.resolve(job),
        text: () => Promise.resolve(JSON.stringify(job)),
      });
    }

    // fetchSyncNowUsage - URL contains scheduleNow/usage
    if (urlStr.includes('scheduleNow') || urlStr.includes('ScheduleNow')) {
      if (usage) {
        return Promise.resolve({
          ok: true,
          status: 200,
          json: () => Promise.resolve(usage),
          text: () => Promise.resolve(JSON.stringify(usage)),
        });
      }
      return Promise.resolve({
        ok: false,
        status: 500,
        json: () => Promise.resolve({}),
        text: () => Promise.resolve(''),
      });
    }

    // Default: return empty success for other calls (group details, photos, etc.)
    return Promise.resolve({
      ok: true,
      status: 200,
      json: () => Promise.resolve({}),
      text: () => Promise.resolve(''),
    });
  });
}

function renderJobDetails(preloadedState: ReturnType<typeof createBaseState>, fetchMock?: ReturnType<typeof vi.fn>) {
  if (fetchMock) {
    global.fetch = fetchMock;
  }
  return renderWithProviders(
    <MemoryRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <JobDetails />
    </MemoryRouter>,
    {
      preloadedState: preloadedState as any,
      serviceMocks: { authenticationService: mockAuthService as any },
    }
  );
}

describe('JobDetails - Sync Now feature', () => {
  beforeEach(() => {
    global.fetch = createFetchMock({ count: 1, limit: 3, remaining: 2 });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('does not render Sync Now button when user is not a submission reviewer', async () => {
    global.fetch = createFetchMock({ count: 1, limit: 3, remaining: 2 });
    renderJobDetails(createBaseState({ isSubmissionReviewer: false }));
    // Wait for page to render with job data
    await screen.findAllByText('Test Group');
    expect(screen.queryByRole('button', { name: /run a sync now/i })).not.toBeInTheDocument();
  });

  it('does not render Sync Now button when job is not idle', async () => {
    global.fetch = createFetchMock({ count: 1, limit: 3, remaining: 2 }, { status: 'InProgress' });
    renderJobDetails(createBaseState({ isSubmissionReviewer: true, jobStatus: 'InProgress' }));
    await screen.findAllByText('Test Group');
    expect(screen.queryByRole('button', { name: /run a sync now/i })).not.toBeInTheDocument();
  });

  it('does not render Sync Now button when job is disabled', async () => {
    // processJob sets enabledOrNot=false for statuses other than Idle/InProgress
    global.fetch = createFetchMock({ count: 1, limit: 3, remaining: 2 }, { status: 'CustomerPaused' });
    renderJobDetails(createBaseState({ isSubmissionReviewer: true, jobStatus: 'CustomerPaused', jobEnabled: false }));
    await screen.findAllByText('Test Group');
    expect(screen.queryByRole('button', { name: /run a sync now/i })).not.toBeInTheDocument();
  });

  it('renders Sync Now button when user is submission reviewer and job is idle and enabled', async () => {
    renderJobDetails(createBaseState({ isSubmissionReviewer: true, jobStatus: 'Idle', jobEnabled: true }));
    const button = await screen.findByRole('button', { name: /run a sync now/i });
    expect(button).toBeInTheDocument();
  });

  it('opens dialog when Sync Now button is clicked', async () => {
    renderJobDetails(createBaseState());
    const button = await screen.findByRole('button', { name: /run a sync now/i });
    fireEvent.click(button);
    await waitFor(() => {
      expect(screen.getByText(/will start within the next five minutes/i)).toBeInTheDocument();
    });
  });

  it('dialog shows usage info when API returns valid data', async () => {
    global.fetch = createFetchMock({ count: 1, limit: 3, remaining: 2 });
    renderJobDetails(createBaseState());
    const button = await screen.findByRole('button', { name: /run a sync now/i });
    // Wait for fetchSyncNowUsage to complete
    await act(async () => { await new Promise(r => setTimeout(r, 50)); });
    fireEvent.click(button);
    await waitFor(() => {
      expect(screen.getByText(/You have used 1 out of 3/i)).toBeInTheDocument();
    });
  });

  it('dialog shows unavailable message when API fails', async () => {
    global.fetch = createFetchMock(null);
    renderJobDetails(createBaseState());
    const button = await screen.findByRole('button', { name: /run a sync now/i });
    fireEvent.click(button);
    await waitFor(() => {
      expect(screen.getByText(/usage information is currently unavailable/i)).toBeInTheDocument();
    });
  });

  it('shows rate limit error when usage count equals limit', async () => {
    global.fetch = createFetchMock({ count: 3, limit: 3, remaining: 0 });
    renderJobDetails(createBaseState());
    const button = await screen.findByRole('button', { name: /run a sync now/i });
    // Wait for fetchSyncNowUsage to complete and set count=3, limit=3
    await act(async () => { await new Promise(r => setTimeout(r, 50)); });
    fireEvent.click(button);
    await waitFor(() => {
      expect(screen.getByText(/exceeded the daily limit/i)).toBeInTheDocument();
    });
    // Dialog should not open - verify no dialog heading exists
    expect(screen.queryByRole('heading', { name: 'Sync now' })).not.toBeInTheDocument();
  });

  it('allows opening dialog when usage data is unavailable (count = -1)', async () => {
    global.fetch = createFetchMock(null);
    renderJobDetails(createBaseState());
    const button = await screen.findByRole('button', { name: /run a sync now/i });
    // Wait for fetchSyncNowUsage to complete (returns -1)
    await act(async () => { await new Promise(r => setTimeout(r, 50)); });
    fireEvent.click(button);
    await waitFor(() => {
      expect(screen.getByText(/will start within the next five minutes/i)).toBeInTheDocument();
    });
  });

  it('dialog shows checkbox for ignoring threshold', async () => {
    renderJobDetails(createBaseState());
    const button = await screen.findByRole('button', { name: /run a sync now/i });
    fireEvent.click(button);
    await waitFor(() => {
      expect(screen.getByText(/ignore percentage limits/i)).toBeInTheDocument();
    });
  });

  it('sends PATCH request with ScheduledNow change reason when confirmed', async () => {
    const fetchMock = createFetchMock({ count: 0, limit: 3, remaining: 3 });
    renderJobDetails(createBaseState(), fetchMock);
    const button = await screen.findByRole('button', { name: /run a sync now/i });
    await act(async () => { await new Promise(r => setTimeout(r, 50)); });
    fireEvent.click(button);
    await waitFor(() => {
      expect(screen.getByText(/will start within the next five minutes/i)).toBeInTheDocument();
    });

    // Click the confirm button
    const confirmButton = screen.getByRole('button', { name: /run sync now/i });
    await act(async () => {
      fireEvent.click(confirmButton);
      await new Promise(r => setTimeout(r, 100));
    });

    // Verify a PATCH call was made with scheduleNow in the URL
    const patchCalls = fetchMock.mock.calls.filter(
      ([url, opts]: [string, RequestInit | undefined]) =>
        opts?.method === 'PATCH' && String(url).includes('scheduleNow')
    );
    expect(patchCalls.length).toBe(1);

    const body = JSON.parse(patchCalls[0][1]?.body as string);
    expect(body.changeReason).toBe('ScheduledNow');
  });

  it('closes dialog when cancel is clicked', async () => {
    renderJobDetails(createBaseState());
    const button = await screen.findByRole('button', { name: /run a sync now/i });
    fireEvent.click(button);
    await waitFor(() => {
      expect(screen.getByText(/will start within the next five minutes/i)).toBeInTheDocument();
    });

    const cancelButton = screen.getByRole('button', { name: /^cancel$/i });
    fireEvent.click(cancelButton);
    await waitFor(() => {
      expect(screen.queryByText(/will start within the next five minutes/i)).not.toBeInTheDocument();
    });
  });
});
