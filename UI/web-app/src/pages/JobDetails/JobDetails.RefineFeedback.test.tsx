// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest';
import { MemoryRouter } from 'react-router-dom';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { setupStore } from '../../store';
import { GMMApi } from '../../apis/GMMApi';
import { GraphApi } from '../../apis/GraphApi';
import { SettingKey } from '../../models/SettingKey';
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
  getTokenAsync: vi.fn(),
};

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
  enabledOrNot: false,
  status: 'PendingReview',
  period: 24,
  arrow: '',
  estimatedNextRunTime: '',
  endpoints: [],
  requestor: '',
  thresholdPercentageForAdditions: 100,
  thresholdPercentageForRemovals: 20,
};

const createState = (isRefinementEnabled: boolean) => ({
  roles: {
    isSubmissionReviewer: true,
    isJobOwnerEnabler: false,
    isJobOwnerDeleter: false,
    isJobOwnerReader: false,
    isJobOwnerWriter: false,
    isJobTenantReader: false,
    isJobTenantWriter: false,
    isAutoApproverAdministrator: false,
    isCustomMembershipProviderAdministrator: false,
    isOperationsResetAdministrator: false,
    isGeneralSettingsAdministrator: false,
    isFetchingRoles: false,
  },
  settings: {
    settings: isRefinementEnabled
      ? [{ settingKey: SettingKey.IsAIRejectionFeedbackRefinementEnabled, settingValue: 'true' }]
      : [],
    selectedSettingLoading: false,
    selectedSetting: undefined,
    isLoading: false,
    error: undefined,
    patchSettingResponse: undefined,
    patchSettingError: undefined,
    isSaving: false,
    supportEmail: '',
    supportEmailLoading: false,
    supportEmailError: undefined,
    defaultAIPrompt: '',
    defaultAIPromptLoading: false,
  },
  jobs: {
    selectedJob: { ...defaultJob },
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

const patchCalls: string[] = [];

function createFetchMock() {
  return vi.fn().mockImplementation((url: string, options?: RequestInit) => {
    const urlStr = String(url);

    if (options?.method === 'PATCH') {
      patchCalls.push(String(options.body ?? ''));
      return Promise.resolve({
        ok: true,
        status: 204,
        json: () => Promise.resolve({}),
        text: () => Promise.resolve(''),
      });
    }

    if (urlStr.includes('/jobDetails/job/') || urlStr.includes('/job/')) {
      // processJob mutates the job it is given, and Redux Toolkit freezes whatever
      // lands in the store. Hand out a fresh copy per call so a later test is not
      // handed the frozen object from an earlier one.
      const job = { ...defaultJob };
      return Promise.resolve({
        ok: true,
        status: 200,
        json: () => Promise.resolve(job),
        text: () => Promise.resolve(JSON.stringify(job)),
      });
    }

    return Promise.resolve({
      ok: true,
      status: 200,
      json: () => Promise.resolve({}),
      text: () => Promise.resolve(''),
    });
  });
}

type Deferred = {
  promise: Promise<{ data: { refinedText: string } }>;
  resolve: (refinedText: string) => void;
  reject: (error: unknown) => void;
};

const createDeferred = (): Deferred => {
  let resolve!: (refinedText: string) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<{ data: { refinedText: string } }>((res, rej) => {
    resolve = (refinedText: string) => res({ data: { refinedText } });
    reject = rej;
  });
  return { promise, resolve, reject };
};

const axiosErrorWith = (status: number, code: string) => ({
  isAxiosError: true,
  response: { status, data: { code, error: 'Feedback refinement failed.' } },
});

function renderJobDetails(isRefinementEnabled = true) {
  const refineFeedbackMock = vi.fn();
  const getTokenAsync = async () => 'mock-token';

  // Only the feedback client is replaced so every other API keeps its real behavior.
  const realGmmApi = new GMMApi({ baseUrl: 'http://localhost/api/v1', getTokenAsync });
  const gmmApi = new Proxy(realGmmApi, {
    get: (target, property, receiver) =>
      property === 'feedback'
        ? { refineFeedback: refineFeedbackMock }
        : Reflect.get(target, property, receiver),
  });

  const store = setupStore(
    createState(isRefinementEnabled) as any,
    { authenticationService: mockAuthService as any },
    {
      gmmApi: gmmApi as any,
      graphApi: new GraphApi({ baseUrl: 'http://localhost/graph/v1.0', getTokenAsync }),
    }
  );

  const view = renderWithProviders(
    <MemoryRouter>
      <JobDetails />
    </MemoryRouter>,
    { store }
  );

  return { ...view, refineFeedbackMock, store };
}

const openRejectionDialog = async () => {
  fireEvent.click(await screen.findByRole('button', { name: 'Reject' }));
  return await screen.findByRole('textbox', { name: /rejection reason/i });
};

const getRefineButton = () => screen.getByRole('button', { name: /refine feedback/i });

const type = (editor: HTMLElement, value: string) => {
  fireEvent.change(editor, { target: { value } });
};

describe('JobDetails - rejection feedback refinement', () => {
  beforeEach(() => {
    patchCalls.length = 0;
    mockAuthService.getTokenAsync.mockResolvedValue('mock-token');
    global.fetch = createFetchMock();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('does not render Refine when the feature setting is absent', async () => {
    renderJobDetails(false);
    await openRejectionDialog();

    expect(screen.queryByRole('button', { name: /refine feedback/i })).not.toBeInTheDocument();
  });

  it('disables Refine and shows guidance for empty or whitespace feedback', async () => {
    renderJobDetails();
    const editor = await openRejectionDialog();

    expect(getRefineButton()).toBeDisabled();
    expect(screen.getByText('Enter feedback before refining it.')).toBeInTheDocument();

    type(editor, '   ');
    expect(getRefineButton()).toBeDisabled();

    type(editor, 'needs work');
    await waitFor(() => expect(getRefineButton()).toBeEnabled());
  });

  it('replaces the editor on success and restores the exact pre-refinement text', async () => {
    const { refineFeedbackMock } = renderJobDetails();
    const editor = await openRejectionDialog();

    refineFeedbackMock.mockResolvedValue({ data: { refinedText: 'Please clarify each exclusion.' } });
    type(editor, 'exclusions unclear');
    fireEvent.click(getRefineButton());

    await waitFor(() => expect(editor).toHaveValue('Please clarify each exclusion.'));
    expect(refineFeedbackMock).toHaveBeenCalledWith('exclusions unclear');
    expect(editor).toBeEnabled();

    fireEvent.click(screen.getByRole('button', { name: /restore original/i }));
    expect(editor).toHaveValue('exclusions unclear');
    // Restore is one-shot.
    expect(screen.queryByRole('button', { name: /restore original/i })).not.toBeInTheDocument();
    expect(patchCalls).toHaveLength(0);
  });

  it('sends the currently displayed text on a repeated refinement', async () => {
    const { refineFeedbackMock } = renderJobDetails();
    const editor = await openRejectionDialog();

    refineFeedbackMock.mockResolvedValue({ data: { refinedText: 'First refinement.' } });
    type(editor, 'first');
    fireEvent.click(getRefineButton());
    await waitFor(() => expect(editor).toHaveValue('First refinement.'));

    refineFeedbackMock.mockResolvedValue({ data: { refinedText: 'Second refinement.' } });
    fireEvent.click(getRefineButton());
    await waitFor(() => expect(editor).toHaveValue('Second refinement.'));

    expect(refineFeedbackMock).toHaveBeenLastCalledWith('First refinement.');
    expect(patchCalls).toHaveLength(0);
  });

  it.each([
    ['RefinedTextTooLong', 422, /too long/i],
    ['FeedbackTooLong', 400, /too long to refine/i],
    ['Timeout', 408, /took too long/i],
    ['ServiceUnavailable', 503, /unavailable right now/i],
    ['InternalError', 500, /could not be refined/i],
  ])('preserves the editor and offers retry when refinement fails with %s', async (code, status, expectedMessage) => {
    const { refineFeedbackMock } = renderJobDetails();
    const editor = await openRejectionDialog();

    refineFeedbackMock.mockRejectedValue(axiosErrorWith(status as number, code as string));
    type(editor, 'original feedback');
    fireEvent.click(getRefineButton());

    expect(await screen.findByText(expectedMessage)).toBeInTheDocument();
    expect(editor).toHaveValue('original feedback');
    await waitFor(() => expect(getRefineButton()).toBeEnabled());
    expect(patchCalls).toHaveLength(0);
  });

  it('keeps the prior Restore snapshot when a later attempt fails', async () => {
    const { refineFeedbackMock } = renderJobDetails();
    const editor = await openRejectionDialog();

    refineFeedbackMock.mockResolvedValue({ data: { refinedText: 'Refined once.' } });
    type(editor, 'original feedback');
    fireEvent.click(getRefineButton());
    await waitFor(() => expect(editor).toHaveValue('Refined once.'));

    refineFeedbackMock.mockRejectedValue(axiosErrorWith(503, 'ServiceUnavailable'));
    fireEvent.click(getRefineButton());
    await screen.findByText(/unavailable right now/i);

    expect(editor).toHaveValue('Refined once.');
    fireEvent.click(screen.getByRole('button', { name: /restore original/i }));
    expect(editor).toHaveValue('original feedback');
  });

  it('discards a stale success when the reviewer edits during refinement', async () => {
    const { refineFeedbackMock } = renderJobDetails();
    const editor = await openRejectionDialog();

    const deferred = createDeferred();
    refineFeedbackMock.mockReturnValue(deferred.promise);
    type(editor, 'original feedback');
    fireEvent.click(getRefineButton());

    type(editor, 'edited while refining');
    deferred.resolve('Stale refinement.');

    expect(await screen.findByText(/changed while it was being refined/i)).toBeInTheDocument();
    expect(editor).toHaveValue('edited while refining');
    expect(screen.queryByRole('button', { name: /restore original/i })).not.toBeInTheDocument();
    await waitFor(() => expect(getRefineButton()).toBeEnabled());
  });

  it('discards a stale failure without showing an obsolete error', async () => {
    const { refineFeedbackMock } = renderJobDetails();
    const editor = await openRejectionDialog();

    const deferred = createDeferred();
    refineFeedbackMock.mockReturnValue(deferred.promise);
    type(editor, 'original feedback');
    fireEvent.click(getRefineButton());

    type(editor, 'edited while refining');
    deferred.reject(axiosErrorWith(503, 'ServiceUnavailable'));

    expect(await screen.findByText(/changed while it was being refined/i)).toBeInTheDocument();
    expect(screen.queryByText(/unavailable right now/i)).not.toBeInTheDocument();
    expect(editor).toHaveValue('edited while refining');
  });

  it('discards the result after an edit that reverts to the original text', async () => {
    const { refineFeedbackMock } = renderJobDetails();
    const editor = await openRejectionDialog();

    const deferred = createDeferred();
    refineFeedbackMock.mockReturnValue(deferred.promise);
    type(editor, 'original feedback');
    fireEvent.click(getRefineButton());

    type(editor, 'changed');
    type(editor, 'original feedback');
    deferred.resolve('Stale refinement.');

    expect(await screen.findByText(/changed while it was being refined/i)).toBeInTheDocument();
    expect(editor).toHaveValue('original feedback');
  });

  it('submits the exact editor text when the reviewer submits during refinement', async () => {
    const { refineFeedbackMock } = renderJobDetails();
    const editor = await openRejectionDialog();

    const deferred = createDeferred();
    refineFeedbackMock.mockReturnValue(deferred.promise);
    type(editor, 'submitted feedback');
    fireEvent.click(getRefineButton());

    fireEvent.click(screen.getByRole('button', { name: 'Submit' }));
    await waitFor(() => expect(patchCalls).toHaveLength(1));
    expect(patchCalls[0]).toContain('submitted feedback');

    deferred.resolve('Refinement that must be ignored.');
    await waitFor(() => expect(patchCalls).toHaveLength(1));
    expect(patchCalls[0]).not.toContain('must be ignored');
    expect(screen.queryByText(/could not be refined/i)).not.toBeInTheDocument();
  });

  it('ignores a pending result after the dialog is closed and reopened', async () => {
    const { refineFeedbackMock } = renderJobDetails();
    const editor = await openRejectionDialog();

    const deferred = createDeferred();
    refineFeedbackMock.mockReturnValue(deferred.promise);
    type(editor, 'first dialog feedback');
    fireEvent.click(getRefineButton());

    fireEvent.click(screen.getByRole('button', { name: /close/i }));
    const reopenedEditor = await openRejectionDialog();
    type(reopenedEditor, 'second dialog feedback');

    deferred.resolve('Refinement from the old dialog.');

    await waitFor(() => expect(reopenedEditor).toHaveValue('second dialog feedback'));
    expect(screen.queryByRole('button', { name: /restore original/i })).not.toBeInTheDocument();
    expect(screen.queryByText(/changed while it was being refined/i)).not.toBeInTheDocument();
  });

  it('never changes review state through Refine or Restore', async () => {
    const { refineFeedbackMock } = renderJobDetails();
    const editor = await openRejectionDialog();

    refineFeedbackMock.mockResolvedValue({ data: { refinedText: 'Refined.' } });
    type(editor, 'original feedback');
    fireEvent.click(getRefineButton());
    await waitFor(() => expect(editor).toHaveValue('Refined.'));
    fireEvent.click(screen.getByRole('button', { name: /restore original/i }));

    expect(patchCalls).toHaveLength(0);
  });
});
