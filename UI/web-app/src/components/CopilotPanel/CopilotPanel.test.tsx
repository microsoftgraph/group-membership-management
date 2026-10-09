// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { initializeIcons } from '@fluentui/react';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { OfflineAuthenticationService } from '../../testing/OfflineAuthenticationService';
import { SourcePartType } from '../../models/SourcePartType';
import type { ISourcePart } from '../../models/ISourcePart';

vi.mock('uuid', () => ({ v4: () => 'mock-uuid' }));

const localStorageMock = {
  getItem: vi.fn(() => null),
  setItem: vi.fn(),
  removeItem: vi.fn(),
  clear: vi.fn(),
  key: vi.fn(() => null),
  length: 0,
};

Object.defineProperty(globalThis, 'localStorage', {
  value: localStorageMock,
  configurable: true,
});

const mockSourcePart = {
  id: 'copilot-part-1',
  title: 'FTEs',
  query: {
    type: SourcePartType.HR,
    source: {
      filter: "EmployeeType_Code = 'FTE'",
    },
    exclusionary: false,
  },
  isNew: true,
  isExpanded: false,
  useOrgStructure: false,
} satisfies ISourcePart;

let cachedModules:
  | Promise<{
      defaultStrings: any;
      renderWithProviders: any;
      setupStore: any;
      CopilotPanel: any;
    }>
  | undefined;

const loadModules = async () => {
  if (!cachedModules) {
    cachedModules = Promise.all([
      import('../../services/localization'),
      import('../../testing/renderWithProviders'),
      import('../../store'),
      import('./CopilotPanel'),
    ]).then(([localization, testing, store, component]) => ({
      defaultStrings: localization.defaultStrings,
      renderWithProviders: testing.renderWithProviders,
      setupStore: store.setupStore,
      CopilotPanel: component.CopilotPanel,
    }));
  }

  return cachedModules;
};

beforeAll(() => {
  initializeIcons(undefined, { disableWarnings: true });
  window.HTMLElement.prototype.scrollIntoView = vi.fn();
});

afterEach(() => {
  vi.unstubAllGlobals();
});

const buildPreloadedState = async (overrides: Record<string, any> = {}) => {
  const { setupStore, defaultStrings } = await loadModules();
  const baseState = setupStore().getState();
  const {
    copilot,
    localization,
    manageMembership,
    orgLeaderDetails,
    ...rest
  } = overrides;

  return {
    ...baseState,
    ...rest,
    copilot: {
      ...baseState.copilot,
      messages: [],
      isLoading: false,
      error: null,
      isPanelOpen: false,
      lastSourceParts: [],
      lastAppliedOperations: [],
      useOrgStructure: false,
      ...copilot,
    },
    localization: {
      ...baseState.localization,
      language: 'en',
      strings: defaultStrings,
      ...localization,
    },
    manageMembership: {
      ...baseState.manageMembership,
      sourceParts: [],
      ...manageMembership,
    },
    orgLeaderDetails: {
      ...baseState.orgLeaderDetails,
      ...orgLeaderDetails,
    },
  };
};

const renderCopilotPanel = async ({
  isOpen = true,
  preloadedState = {},
  dismissPanel = vi.fn(),
  experience = 'onboarding',
}: {
  isOpen?: boolean;
  preloadedState?: Record<string, any>;
  dismissPanel?: () => void;
  experience?: 'onboarding' | 'update';
} = {}) => {
  const { renderWithProviders, CopilotPanel } = await loadModules();
  const result = renderWithProviders(
    <CopilotPanel isOpen={isOpen} dismissPanel={dismissPanel} experience={experience} />,
    {
      preloadedState: await buildPreloadedState(preloadedState),
      serviceMocks: { authenticationService: new OfflineAuthenticationService() },
    }
  );

  return {
    dismissPanel,
    ...result,
  };
};

describe('CopilotPanel', () => {
  it.each(['rejects', 'returns nothing'])('when apply %s, keeps the proposal until apply settles', async (kind) => {
    const impl = () => (kind === 'rejects' ? Promise.reject(new Error('Apply failed')) : Promise.resolve(undefined));
    const { defaultStrings, setupStore, renderWithProviders, CopilotPanel } = await loadModules();
    const store = setupStore(await buildPreloadedState({
      copilot: {
        messages: [{ id: 'a', role: 'assistant', content: 'Proposal.', timestamp: '2026-10-07T20:30:00.000Z' }],
        lastSourceParts: [mockSourcePart],
        lastAppliedOperations: [{ op: 'add', partId: mockSourcePart.id }],
      },
    }), { authenticationService: new OfflineAuthenticationService() });
    const dismissPanel = vi.fn();
    const apply = vi.fn(impl as () => Promise<never>);
    renderWithProviders(<CopilotPanel isOpen dismissPanel={dismissPanel} onSourcePartsGenerated={apply} />, { store });
    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.resumeContinue }));
    fireEvent.click(await screen.findByRole('button', { name: defaultStrings.Copilot.applyAndReview }));
    await waitFor(() => expect(apply).toHaveBeenCalledOnce());
    if (kind === 'rejects') {
      expect(await screen.findByText(defaultStrings.Copilot.errorMessage)).toBeInTheDocument();
      expect(dismissPanel).not.toHaveBeenCalled();
      expect(store.getState().copilot.lastSourceParts).toHaveLength(1);
    } else {
      await waitFor(() => expect(dismissPanel).toHaveBeenCalledOnce());
      expect(store.getState().copilot.error).toBeNull();
    }
  });

  it.each(['onboarding', 'update'] as const)('reopens %s after apply with the complete accepted summary and inline actions', async (experience) => {
    const { defaultStrings, setupStore, renderWithProviders, CopilotPanel } = await loadModules();
    const { setSourceParts } = await import('../../store/manageMembership.slice');
    const { addMessage } = await import('../../store/copilot.slice');
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
      message: 'Your current membership includes contractors.',
      resultingQuery: [],
      appliedOperations: [],
    }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    const store = setupStore(await buildPreloadedState({
      copilot: {
        messages: [{
          id: 'proposal',
          role: 'assistant',
          content: "Ready.\n\n**Proposed Membership:** Includes **FTEs** in the United States.\nAlso includes members of **IT Infrastructure**.\n\nClick **Accept & Apply** to review.",
          timestamp: '2026-10-07T20:30:00.000Z',
        }],
        lastSourceParts: [mockSourcePart],
        lastAppliedOperations: [{ op: 'add', partId: mockSourcePart.id }],
      },
    }), { authenticationService: new OfflineAuthenticationService() });
    const apply = vi.fn(async (parts) => {
      store.dispatch(setSourceParts(parts));
      return parts;
    });
    const Host = () => {
      const [open, setOpen] = React.useState(true);
      return <>
        <button onClick={() => setOpen(true)}>Reopen panel</button>
        <CopilotPanel isOpen={open} experience={experience} dismissPanel={() => setOpen(false)} onSourcePartsGenerated={apply} />
      </>;
    };
    renderWithProviders(<Host />, { store });
    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.resumeContinue }));
    expect(screen.queryByRole('button', { name: 'Accept & Apply' })).not.toBeInTheDocument();
    fireEvent.click(await screen.findByRole('button', { name: defaultStrings.Copilot.applyAndReview }));
    await waitFor(() => expect(apply).toHaveBeenCalledOnce());
    await waitFor(() => expect(screen.queryByText(defaultStrings.Copilot.title)).not.toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Reopen panel' }));
    const summary = await screen.findByTestId('copilot-applied-summary');
    expect(screen.getAllByTestId('copilot-apply-action')).toHaveLength(1);
    expect(screen.getByTestId('copilot-apply-action')).toHaveTextContent(defaultStrings.Copilot.applyAndReview);
    expect(store.getState().copilot.messages.at(-1)).toMatchObject({ role: 'user', action: 'applyAndReview' });
    expect(summary).toHaveTextContent('Includes FTEs in the United States.');
    expect(summary).toHaveTextContent('Also includes members of IT Infrastructure.');
    expect(summary).not.toHaveTextContent('Click Accept & Apply');
    expect(summary.querySelector('strong')).toHaveTextContent(defaultStrings.Copilot.currentMembership);
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    const resume = within(screen.getByTestId('copilot-update-resume'));
    const resumeText = screen.getByTestId('copilot-update-resume').textContent;
    fireEvent.click(resume.getByRole('button', { name: defaultStrings.Copilot.resumeContinue }));
    expect(screen.getByPlaceholderText(defaultStrings.Copilot.inputPlaceholder)).toHaveFocus();
    expect(screen.getByTestId('copilot-applied-summary')).toBe(summary);
    expect(resume.queryAllByRole('button')).toHaveLength(0);
    expect(resumeText).toBe(screen.getByTestId('copilot-update-resume').textContent
      + defaultStrings.Copilot.resumeContinue + defaultStrings.Copilot.newConversation);
    act(() => { store.dispatch(addMessage({
      id: 'unaccepted',
      role: 'assistant',
      content: '**Proposed Membership:** A different proposal not yet applied.',
      timestamp: '2026-10-07T20:31:00.000Z',
    })); });
    expect(screen.getByTestId('copilot-applied-summary')).toHaveTextContent('Includes FTEs in the United States.');
    expect(screen.getByTestId('copilot-update-resume').compareDocumentPosition(
      screen.getByText('A different proposal not yet applied.')) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.closeButton }));
    await waitFor(() => expect(screen.queryByText(defaultStrings.Copilot.title)).not.toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Reopen panel' }));
    expect(await screen.findByTestId('copilot-applied-summary')).not.toHaveTextContent('A different proposal');
    fireEvent.click(within(screen.getByTestId('copilot-update-resume')).getByRole('button', { name: defaultStrings.Copilot.newConversation }));
    const cachedSummary = screen.getByTestId('copilot-current-membership');
    expect(cachedSummary).toHaveTextContent('Includes FTEs in the United States.');
    expect(cachedSummary).toHaveTextContent('Also includes members of IT Infrastructure.');
    expect(cachedSummary).not.toHaveTextContent('A different proposal');
    expect(screen.queryByText(defaultStrings.Copilot.updateResumeTitle)).not.toBeInTheDocument();
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(store.getState().copilot.messages).toHaveLength(1);
    expect(store.getState().manageMembership.sourceParts).toEqual([mockSourcePart]);
    fireEvent.click(within(cachedSummary).getByRole('button', { name: defaultStrings.Copilot.newConversation }));
    expect(screen.getByTestId('copilot-current-membership')).toHaveTextContent('Includes FTEs in the United States.');
    expect(fetchMock).not.toHaveBeenCalled();
    const changedPart = {
      ...mockSourcePart,
      query: { ...mockSourcePart.query, source: { filter: "EmployeeType_Code = 'CONTRACTOR'" } },
    };
    act(() => { store.dispatch(setSourceParts([changedPart])); });
    fireEvent.click(within(screen.getByTestId('copilot-current-membership')).getByRole('button', { name: defaultStrings.Copilot.newConversation }));
    await waitFor(() => expect(screen.getByTestId('copilot-current-membership')).toHaveTextContent('Includes contractors.'));
    expect(fetchMock).toHaveBeenCalledOnce();
    const body = fetchMock.mock.calls[0][1]?.body;
    if (typeof body !== 'string') throw new Error('Expected a JSON request body');
    expect(JSON.parse(body).workingQuery[0].filter).toBe("EmployeeType_Code = 'CONTRACTOR'");
    expect(store.getState().manageMembership.sourceParts).toEqual([changedPart]);
    const newerPart = {
      ...mockSourcePart,
      query: { ...mockSourcePart.query, source: { filter: "EmployeeType_Code = 'INTERN'" } },
    };
    fetchMock.mockResolvedValueOnce(new Response(JSON.stringify({
      message: 'Your current membership includes interns.',
      resultingQuery: [],
      appliedOperations: [],
    }), { status: 200 }));
    act(() => { store.dispatch(setSourceParts([newerPart])); });
    const previousMessages = store.getState().copilot.messages;
    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.closeButton }));
    await waitFor(() => expect(screen.queryByText(defaultStrings.Copilot.title)).not.toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Reopen panel' }));
    expect(screen.getByText(defaultStrings.Copilot.gettingCurrentMembership)).toBeInTheDocument();
    expect(screen.queryByTestId('copilot-applied-summary')).not.toBeInTheDocument();
    expect(await screen.findByTestId('copilot-applied-summary')).toHaveTextContent('Includes interns.');
    expect(store.getState().copilot.messages).toEqual(previousMessages);
    expect(store.getState().manageMembership.sourceParts).toEqual([newerPart]);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    const refreshBody = fetchMock.mock.calls[1][1]?.body;
    if (typeof refreshBody !== 'string') throw new Error('Expected a JSON request body');
    expect(JSON.parse(refreshBody).workingQuery[0].filter).toBe("EmployeeType_Code = 'INTERN'");
    expect(JSON.parse(refreshBody).conversationHistory).toEqual([]);
    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.closeButton }));
    await waitFor(() => expect(screen.queryByText(defaultStrings.Copilot.title)).not.toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Reopen panel' }));
    expect(await screen.findByTestId('copilot-applied-summary')).toHaveTextContent('Includes interns.');
    expect(fetchMock).toHaveBeenCalledTimes(2);
    fetchMock.mockRejectedValueOnce(new Error('Offline'));
    act(() => { store.dispatch(setSourceParts([changedPart])); });
    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.closeButton }));
    await waitFor(() => expect(screen.queryByText(defaultStrings.Copilot.title)).not.toBeInTheDocument());
    fireEvent.click(screen.getByRole('button', { name: 'Reopen panel' }));
    expect(await screen.findByText(defaultStrings.Copilot.errorMessage)).toBeInTheDocument();
    expect(screen.getByTestId('copilot-applied-summary')).toHaveTextContent(defaultStrings.Copilot.appliedMembershipChanged);
    expect(screen.getByTestId('copilot-applied-summary')).not.toHaveTextContent('includes interns');
    expect(store.getState().manageMembership.sourceParts).toEqual([changedPart]);
    expect(store.getState().copilot.messages.slice(0, -1)).toEqual(previousMessages);
    act(() => { store.dispatch(setSourceParts([])); });
    fireEvent.click(within(screen.getByTestId('copilot-update-resume')).getByRole('button', { name: defaultStrings.Copilot.newConversation }));
    expect(screen.queryByTestId('copilot-applied-summary')).not.toBeInTheDocument();
    expect(store.getState().copilot.messages).toEqual([]);
    expect(screen.queryByTestId('copilot-apply-action')).not.toBeInTheDocument();
  }, 15000);

  it('does not render panel content when isOpen is false', async () => {
    const { defaultStrings } = await loadModules();
    await renderCopilotPanel({ isOpen: false });

    expect(screen.queryByText(defaultStrings.Copilot.welcomeMessage)).not.toBeInTheDocument();
  }, 15000);

  it('renders welcome message with steps when open and no messages', async () => {
    const { defaultStrings } = await loadModules();
    await renderCopilotPanel();

    expect(screen.getByText(defaultStrings.Copilot.welcomeMessage)).toBeInTheDocument();
    expect(screen.getByText(defaultStrings.Copilot.welcomeStep1Bold)).toBeInTheDocument();
    expect(screen.getByText(defaultStrings.Copilot.welcomeStep2Bold)).toBeInTheDocument();
    expect(screen.getByText(defaultStrings.Copilot.welcomeStep3Bold)).toBeInTheDocument();
    expect(screen.getByText(defaultStrings.Copilot.welcomeDisclaimer)).toBeInTheDocument();
    expect(screen.getAllByTestId('copilot-avatar')).toHaveLength(2);
    expect(screen.queryByTestId('copilot-mode-indicator')).not.toBeInTheDocument();
    expect(screen.queryByText(defaultStrings.Copilot.tryOneOfTheseToGetStarted)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: defaultStrings.Copilot.newConversation })).not.toBeInTheDocument();
  });

  it('renders the update-specific welcome without changing onboarding copy', async () => {
    const { defaultStrings } = await loadModules();
    await renderCopilotPanel({ experience: 'update' });

    expect(screen.getByText(defaultStrings.Copilot.updateResumeTitle)).toBeInTheDocument();
    expect(screen.getByText(defaultStrings.Copilot.updateResumeDescription)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: defaultStrings.Copilot.resumeContinue })).toBeInTheDocument();
    expect(within(screen.getByTestId('copilot-update-resume')).getByRole('button', { name: defaultStrings.Copilot.newConversation })).toBeInTheDocument();
    expect(screen.queryByText(defaultStrings.Copilot.updateWelcomeTitle)).not.toBeInTheDocument();
    expect(screen.queryByText(defaultStrings.Copilot.welcomeStep1Bold)).not.toBeInTheDocument();
    expect(within(screen.getByTestId('copilot-header')).queryByRole('button', { name: defaultStrings.Copilot.newConversation })).not.toBeInTheDocument();
  });

  it.each(['onboarding', 'update'] as const)('shows inline Continue / New chat instead of a dialog for a returning %s chat before apply', async (experience) => {
    const { defaultStrings } = await loadModules();
    await renderCopilotPanel({
      experience,
      preloadedState: {
        copilot: {
          messages: [{
            id: 'assistant-message',
            role: 'assistant',
            content: 'Your existing membership is ready to refine.',
            timestamp: '2026-01-01T00:00:00.000Z',
          }],
        },
      },
    });

    expect(await screen.findByTestId('copilot-update-resume')).toBeInTheDocument();
    expect(screen.getByText(defaultStrings.Copilot.updateResumeTitle)).toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.queryByText('Resume your chat?')).not.toBeInTheDocument();
    const resume = within(screen.getByTestId('copilot-update-resume'));
    expect(resume.getByRole('button', { name: defaultStrings.Copilot.resumeContinue })).toBeInTheDocument();
    expect(resume.getByRole('button', { name: defaultStrings.Copilot.newConversation })).toBeInTheDocument();
  });

  it.each([
    ['onboarding', 'header'],
    ['onboarding', 'inline'],
    ['update', 'header'],
    ['update', 'inline'],
  ] as const)('starts a %s chat from the %s with a read-only membership summary', async (experience, entry) => {
    const { defaultStrings } = await loadModules();
    const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
      message: 'Your current membership includes **all FTEs**.',
      resultingQuery: [{ partId: mockSourcePart.id, filter: "EmployeeType_Code = 'FTE'", title: 'FTEs' }],
      appliedOperations: [],
    }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    const { store } = await renderCopilotPanel({
      experience,
      preloadedState: {
        copilot: {
          messages: [{
            id: 'user-message',
            role: 'user',
            content: 'Refine this membership.',
            timestamp: '2026-01-01T00:00:00.000Z',
          }],
        },
        manageMembership: {
          sourceParts: [mockSourcePart],
        },
      },
    });

    fireEvent.click(within(screen.getByTestId(entry === 'header' ? 'copilot-header' : 'copilot-update-resume'))
      .getByRole('button', { name: defaultStrings.Copilot.newConversation }));

    expect(store.getState().copilot.messages).toEqual([]);
    expect(store.getState().manageMembership.sourceParts).toEqual([mockSourcePart]);
    expect(screen.getByText(defaultStrings.Copilot.gettingCurrentMembership)).toBeInTheDocument();
    expect(screen.getByRole('progressbar')).toBeInTheDocument();
    expect(screen.getByPlaceholderText(defaultStrings.Copilot.inputPlaceholder)).toBeDisabled();
    const summary = await screen.findByTestId('copilot-current-membership');
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(summary).toHaveTextContent('Includes all FTEs.');
    expect(summary).not.toHaveTextContent('Your current membership includes');
    expect(within(summary).getByTestId('copilot-membership-description')).toHaveStyle({ fontWeight: 700 });
    expect(within(summary).getByTestId('copilot-membership-description')).not.toHaveTextContent(defaultStrings.Copilot.appliedResumeDescription);
    expect(within(summary).getByText('all FTEs').tagName).toBe('STRONG');
    expect(screen.queryByText(defaultStrings.Copilot.updateResumeTitle)).not.toBeInTheDocument();
    expect(screen.queryByTestId('copilot-onboarding-welcome')).not.toBeInTheDocument();
    expect(screen.queryByText('What is my current membership?')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: defaultStrings.Copilot.applyAndReview })).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledOnce();
    const body = fetchMock.mock.calls[0][1]?.body;
    if (typeof body !== 'string') throw new Error('Expected a JSON request body');
    expect(JSON.parse(body)).toMatchObject({
      message: expect.stringContaining('What is my current membership?'),
      conversationHistory: [],
      workingQuery: [{ partId: mockSourcePart.id, filter: "EmployeeType_Code = 'FTE'" }],
    });
    expect(store.getState().copilot.lastSourceParts).toEqual([]);
    expect(store.getState().copilot.messages).toHaveLength(1);
    expect(store.getState().manageMembership.sourceParts).toEqual([mockSourcePart]);
    fireEvent.click(within(summary).getByRole('button', { name: defaultStrings.Copilot.resumeContinue }));
    expect(screen.getByTestId('copilot-current-membership')).toBe(summary);
    expect(within(summary).queryAllByRole('button')).toHaveLength(0);
    expect(screen.getByPlaceholderText(defaultStrings.Copilot.inputPlaceholder)).toHaveFocus();
    expect(fetchMock).toHaveBeenCalledOnce();
  });

  it('shows a summary request failure and lets New chat retry without changing membership', async () => {
    const { defaultStrings } = await loadModules();
    const fetchMock = vi.fn<typeof fetch>()
      .mockRejectedValueOnce(new Error('Offline'))
      .mockResolvedValueOnce(new Response(JSON.stringify({
        message: 'Your current membership includes FTEs.',
        resultingQuery: [],
        appliedOperations: [],
      }), { status: 200 }));
    vi.stubGlobal('fetch', fetchMock);
    const { store } = await renderCopilotPanel({
      experience: 'update',
      preloadedState: { manageMembership: { sourceParts: [mockSourcePart] } },
    });
    expect(await screen.findByText(defaultStrings.Copilot.errorMessage)).toBeInTheDocument();
    expect(screen.queryByTestId('copilot-current-membership')).not.toBeInTheDocument();
    expect(store.getState().manageMembership.sourceParts).toEqual([mockSourcePart]);
    fireEvent.click(within(screen.getByTestId('copilot-update-resume'))
      .getByRole('button', { name: defaultStrings.Copilot.newConversation }));
    expect(await screen.findByTestId('copilot-current-membership')).toHaveTextContent('Includes FTEs.');
    expect(screen.queryByText(defaultStrings.Copilot.errorMessage)).not.toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(store.getState().manageMembership.sourceParts).toEqual([mockSourcePart]);
  });

  it('describes current membership on first update open, then reuses it on reopen and New chat', async () => {
    const { defaultStrings, setupStore, renderWithProviders, CopilotPanel } = await loadModules();
    let resolveResponse!: (response: Response) => void;
    const fetchMock = vi.fn<typeof fetch>().mockImplementation(() => new Promise<Response>(resolve => {
      resolveResponse = resolve;
    }));
    vi.stubGlobal('fetch', fetchMock);
    const store = setupStore(
      await buildPreloadedState({ manageMembership: { sourceParts: [mockSourcePart] } }),
      { authenticationService: new OfflineAuthenticationService() }
    );
    const panel = (isOpen: boolean) => <CopilotPanel isOpen={isOpen} dismissPanel={vi.fn()} experience="update" />;
    const { rerender } = renderWithProviders(panel(false), { store });
    expect(fetchMock).not.toHaveBeenCalled();
    rerender(panel(true));
    expect(screen.getByText(defaultStrings.Copilot.gettingCurrentMembership)).toBeInTheDocument();
    expect(screen.getByPlaceholderText(defaultStrings.Copilot.inputPlaceholder)).toBeDisabled();
    await waitFor(() => expect(fetchMock).toHaveBeenCalledOnce());
    const body = fetchMock.mock.calls[0][1]?.body;
    if (typeof body !== 'string') throw new Error('Expected a JSON request body');
    const request = JSON.parse(body);
    expect(request.conversationHistory).toEqual([]);
    expect(request.workingQuery).toMatchObject([{ partId: mockSourcePart.id, filter: "EmployeeType_Code = 'FTE'" }]);
    expect(request.message).not.toContain(JSON.stringify(request.workingQuery));
    expect(request.message).toContain('What is my current membership?');
    await act(async () => resolveResponse(new Response(JSON.stringify({
      message: 'Includes **all FTEs**.',
      resultingQuery: [],
      appliedOperations: [],
    }), { status: 200 })));
    expect(await screen.findByText('all FTEs')).toBeInTheDocument();
    expect(screen.getByTestId('copilot-membership-description')).toHaveStyle({ fontWeight: 700 });
    expect(screen.getByTestId('copilot-membership-description')).not.toHaveTextContent(defaultStrings.Copilot.appliedResumeDescription);
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: defaultStrings.Copilot.applyAndReview })).not.toBeInTheDocument();
    expect(screen.queryByText('What is my current membership?')).not.toBeInTheDocument();
    expect(store.getState().copilot.modelContext.summary).toBe('Includes **all FTEs**.');
    const resumeText = screen.getByTestId('copilot-update-resume').textContent;
    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.resumeContinue }));
    expect(within(screen.getByTestId('copilot-update-resume')).queryAllByRole('button')).toHaveLength(0);
    expect(resumeText).toBe(screen.getByTestId('copilot-update-resume').textContent
      + defaultStrings.Copilot.resumeContinue + defaultStrings.Copilot.newConversation);
    expect(screen.getAllByText(defaultStrings.Copilot.updateResumeTitle)).toHaveLength(1);
    expect(screen.getByPlaceholderText(defaultStrings.Copilot.inputPlaceholder)).toHaveFocus();
    expect(store.getState().copilot.messages).toEqual([]);
    expect(store.getState().manageMembership.sourceParts).toEqual([mockSourcePart]);
    rerender(panel(false));
    rerender(panel(true));
    expect(screen.getByTestId('copilot-update-resume')).toHaveTextContent('all FTEs');
    expect(fetchMock).toHaveBeenCalledOnce();
    fireEvent.click(within(screen.getByTestId('copilot-update-resume'))
      .getByRole('button', { name: defaultStrings.Copilot.newConversation }));
    expect(await screen.findByTestId('copilot-current-membership')).toHaveTextContent('all FTEs');
    expect(fetchMock).toHaveBeenCalledOnce();
  });

  it('keeps the fresh welcome without a request when New chat has no membership rules', async () => {
    const { defaultStrings } = await loadModules();
    const fetchMock = vi.fn<typeof fetch>();
    vi.stubGlobal('fetch', fetchMock);
    const { store } = await renderCopilotPanel({
      experience: 'update',
    });
    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.newConversation }));
    expect(screen.getByText(defaultStrings.Copilot.updateWelcomeTitle)).toBeInTheDocument();
    expect(screen.queryByTestId('copilot-update-resume')).not.toBeInTheDocument();
    expect(store.getState().manageMembership.sourceParts).toEqual([]);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('renders user and assistant messages from state', async () => {
    await renderCopilotPanel({
      preloadedState: {
        copilot: {
          messages: [
            {
              id: 'user-message',
              role: 'user',
              content: 'Show me FTEs.',
              timestamp: '2026-01-01T00:00:00.000Z',
            },
            {
              id: 'assistant-message',
              role: 'assistant',
              content: 'Here is a filter for FTEs.',
              timestamp: '2026-01-01T00:00:01.000Z',
            },
          ],
        },
      },
    });

    expect(screen.getByText('Show me FTEs.')).toBeInTheDocument();
    expect(screen.getByText('Here is a filter for FTEs.')).toBeInTheDocument();
  });

  it('shows loading spinner when isLoading is true', async () => {
    const { defaultStrings } = await loadModules();
    await renderCopilotPanel({
      preloadedState: {
        copilot: {
          isLoading: true,
        },
      },
    });

    expect(screen.getByText(defaultStrings.Copilot.thinking)).toBeInTheDocument();
  });

  it('displays error message', async () => {
    const { defaultStrings } = await loadModules();
    await renderCopilotPanel({
      preloadedState: {
        copilot: {
          messages: [
            {
              id: 'error-message',
              role: 'assistant',
              content: 'Failed to communicate with GMM Copilot. Please try again.',
              timestamp: '2026-01-01T00:00:02.000Z',
              isError: true,
            },
          ],
          error: 'Failed to communicate with GMM Copilot. Please try again.',
        },
      },
    });

    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.resumeContinue }));

    expect(await screen.findByText(defaultStrings.Copilot.errorMessage)).toBeInTheDocument();
  });

  it('shows Apply & Review button when lastSourceParts exist', async () => {
    const { defaultStrings } = await loadModules();
    await renderCopilotPanel({
      preloadedState: {
        copilot: {
          messages: [
            {
              id: 'assistant-message',
              role: 'assistant',
              content: 'Here is a filter for FTEs.',
              timestamp: '2026-01-01T00:00:01.000Z',
            },
          ],
          lastSourceParts: [mockSourcePart],
          lastAppliedOperations: [{ op: 'add', partId: 'p1' }],
        },
      },
    });

    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.resumeContinue }));

    expect(
      await screen.findByRole('button', { name: defaultStrings.Copilot.applyAndReview })
    ).toBeInTheDocument();
  });

  it('hides Apply & Review button on a no-op turn (no applied operations)', async () => {
    // Describe/clarify/refuse turns return the query unchanged (lastSourceParts populated) but
    // with no applied operations. Offering Apply & Review would re-apply an identical query.
    const { defaultStrings } = await loadModules();
    await renderCopilotPanel({
      preloadedState: {
        copilot: {
          messages: [
            {
              id: 'assistant-message',
              role: 'assistant',
              content: 'I do not see a vertical attribute in your data.',
              timestamp: '2026-01-01T00:00:01.000Z',
            },
          ],
          lastSourceParts: [mockSourcePart],
          lastAppliedOperations: [],
        },
      },
    });

    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.resumeContinue }));

    expect(
      screen.queryByRole('button', { name: defaultStrings.Copilot.applyAndReview })
    ).not.toBeInTheDocument();
  });

  it('keeps Apply & Review visible on the last bot message when only errors follow it', async () => {
    const { defaultStrings } = await loadModules();
    await renderCopilotPanel({
      preloadedState: {
        copilot: {
          messages: [
            {
              id: 'assistant-message',
              role: 'assistant',
              content: 'Here is a filter for FTEs.',
              timestamp: '2026-01-01T00:00:01.000Z',
            },
            {
              id: 'error-message',
              role: 'assistant',
              content: 'Failed to communicate with GMM Copilot. Please try again.',
              timestamp: '2026-01-01T00:00:02.000Z',
              isError: true,
            },
          ],
          lastSourceParts: [mockSourcePart],
          lastAppliedOperations: [{ op: 'add', partId: 'p1' }],
        },
      },
    });

    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.resumeContinue }));

    expect(
      await screen.findByRole('button', { name: defaultStrings.Copilot.applyAndReview })
    ).toBeInTheDocument();
  });

  it('calls dismissPanel on close button click', async () => {
    const { defaultStrings } = await loadModules();
    const dismissPanel = vi.fn();
    await renderCopilotPanel({ dismissPanel });

    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.closeButton }));

    expect(dismissPanel).toHaveBeenCalledTimes(1);
  });

  it('calls clearMessages on new conversation button', async () => {
    const { defaultStrings } = await loadModules();
    const { store } = await renderCopilotPanel({
      preloadedState: {
        copilot: {
          messages: [
            {
              id: 'user-message',
              role: 'user',
              content: 'Show me FTEs.',
              timestamp: '2026-01-01T00:00:00.000Z',
            },
          ],
          error: 'Old error',
          lastSourceParts: [mockSourcePart],
          useOrgStructure: true,
        },
      },
    });
    
    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.resumeContinue }));
    
    fireEvent.click(
      await screen.findByRole('button', { name: defaultStrings.Copilot.newConversation })
    );
    
    expect(store.getState().copilot.messages).toEqual([]);
    expect(store.getState().copilot.error).toBeNull();
    expect(store.getState().copilot.lastSourceParts).toEqual([]);
    expect(store.getState().copilot.useOrgStructure).toBe(false);
    expect(screen.queryByRole('button', { name: defaultStrings.Copilot.newConversation })).not.toBeInTheDocument();
  });

  describe('copy conversation', () => {
  const setupClipboardMock = () => {
  const writeText = vi.fn().mockResolvedValue(undefined);
  Object.defineProperty(navigator, 'clipboard', {
    configurable: true,
    value: { writeText },
  });
  return writeText;
  };

  it('copies conversation JSON to clipboard when messages exist', async () => {
  const { defaultStrings } = await loadModules();
  const writeText = setupClipboardMock();
  const messages = [
    {
      id: 'user-message',
      role: 'user',
      content: 'Show me FTEs.',
      timestamp: '2026-01-01T00:00:00.000Z',
    },
    {
      id: 'assistant-message',
      role: 'assistant',
      content: 'Here is a filter for FTEs.',
      timestamp: '2026-01-01T00:00:01.000Z',
    },
  ];
  await renderCopilotPanel({
    preloadedState: {
      copilot: { messages },
    },
  });

  fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.resumeContinue }));

  const copyButton = await screen.findByRole('button', { name: defaultStrings.Copilot.copyConversation });
  expect(copyButton).toBeEnabled();

  fireEvent.click(copyButton);

  await waitFor(() => expect(writeText).toHaveBeenCalledTimes(1));
  const written = writeText.mock.calls[0][0];
  const parsed = JSON.parse(written);
  expect(parsed.messages).toEqual(messages);
  expect(parsed.messageCount).toBe(messages.length);
  expect(typeof parsed.sessionId).toBe('string');
  expect(parsed.sessionId.length).toBeGreaterThan(0);
  expect(typeof parsed.copiedAt).toBe('string');
  expect(parsed.error).toBeNull();
  expect(parsed.warning).toBeNull();
  });

  it('includes the session id and error banner in the copied diagnostics', async () => {
  const { defaultStrings } = await loadModules();
  const writeText = setupClipboardMock();
  const messages = [
    {
      id: 'user-message',
      role: 'user',
      content: 'Show me FTEs.',
      timestamp: '2026-01-01T00:00:00.000Z',
    },
  ];
  await renderCopilotPanel({
    preloadedState: {
      copilot: {
        messages,
        conversationId: 'session-abc-123',
        error: 'Failed to communicate with GMM Copilot. Please try again.',
      },
    },
  });

  fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.resumeContinue }));

  const copyButton = await screen.findByRole('button', { name: defaultStrings.Copilot.copyConversation });
  fireEvent.click(copyButton);

  await waitFor(() => expect(writeText).toHaveBeenCalledTimes(1));
  const parsed = JSON.parse(writeText.mock.calls[0][0]);
  expect(parsed.sessionId).toBe('session-abc-123');
  expect(parsed.error).toBe('Failed to communicate with GMM Copilot. Please try again.');
  expect(parsed.messages).toEqual(messages);
  });
  });

  describe('suggested prompts', () => {
    const validPromptsJson = JSON.stringify([
      { label: 'Include reports', prompt: 'Include all reports who roll up to an employee' },
      { label: 'Include group members', prompt: 'Include all members of a specific group' },
    ]);

    it.each(['onboarding', 'update'] as const)('hides suggestions when a %s chat starts and restores them after reset', async (experience) => {
      const { defaultStrings } = await loadModules();
      const { addMessage, setError } = await import('../../store/copilot.slice');
      const { store } = await renderCopilotPanel({
        experience,
        preloadedState: {
          settings: { settings: [{ settingKey: 15, settingValue: validPromptsJson }] },
        },
      });
      if (experience === 'update') {
        fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.newConversation }));
      }
      expect(screen.getByRole('button', { name: 'Include reports' })).toBeInTheDocument();
      act(() => {
        store.dispatch(addMessage({
          id: 'first-request',
          role: 'user',
          content: 'Include reports',
          timestamp: '2026-10-07T20:17:00.000Z',
        }));
      });
      expect(screen.queryByRole('button', { name: 'Include reports' })).not.toBeInTheDocument();
      act(() => { store.dispatch(setError('Request failed')); });
      expect(screen.queryByRole('button', { name: 'Include group members' })).not.toBeInTheDocument();
      fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.newConversation }));
      expect(screen.getByRole('button', { name: 'Include reports' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Include group members' })).toBeInTheDocument();
    });

    it('renders suggested prompt buttons when settings contain prompts', async () => {
      await renderCopilotPanel({
        preloadedState: {
          settings: {
            settings: [
              { settingKey: 15, settingValue: validPromptsJson },
            ],
          },
        },
      });

      expect(screen.getByText('Include reports')).toBeInTheDocument();
      expect(screen.getByText('Include group members')).toBeInTheDocument();
    });

    it('does not render suggested prompts when setting is empty', async () => {
      const { defaultStrings } = await loadModules();
      await renderCopilotPanel({
        preloadedState: {
          settings: {
            settings: [
              { settingKey: 15, settingValue: '' },
            ],
          },
        },
      });

      expect(screen.queryByText(defaultStrings.Copilot.tryOneOfTheseToGetStarted)).not.toBeInTheDocument();
      expect(screen.queryAllByRole('button', { name: /Include/i })).toHaveLength(0);
    });

    it('handles malformed JSON gracefully', async () => {
      const { defaultStrings } = await loadModules();
      await renderCopilotPanel({
        preloadedState: {
          settings: {
            settings: [
              { settingKey: 15, settingValue: 'not valid json{{{' },
            ],
          },
        },
      });

      expect(screen.queryByText(defaultStrings.Copilot.tryOneOfTheseToGetStarted)).not.toBeInTheDocument();
    });

    it('does not render suggested prompts for empty JSON array', async () => {
      const { defaultStrings } = await loadModules();
      await renderCopilotPanel({
        preloadedState: {
          settings: {
            settings: [
              { settingKey: 15, settingValue: '[]' },
            ],
          },
        },
      });

      expect(screen.queryByText(defaultStrings.Copilot.tryOneOfTheseToGetStarted)).not.toBeInTheDocument();
      expect(screen.queryAllByRole('button', { name: /Include/i })).toHaveLength(0);
    });

    it('does not render suggested prompts when setting is missing', async () => {
      const { defaultStrings } = await loadModules();
      await renderCopilotPanel();

      expect(screen.queryByText(defaultStrings.Copilot.tryOneOfTheseToGetStarted)).not.toBeInTheDocument();
      expect(screen.queryAllByRole('button', { name: /Include/i })).toHaveLength(0);
    });

    it('filters out prompts with non-string label or prompt values', async () => {
      const mixedJson = JSON.stringify([
        { label: 'Valid prompt', prompt: 'Valid prompt text' },
        { label: 42, prompt: 'Number label' },
        { label: 'Missing prompt', prompt: null },
        { label: '', prompt: 'Empty label' },
      ]);

      await renderCopilotPanel({
        preloadedState: {
          settings: {
            settings: [
              { settingKey: 15, settingValue: mixedJson },
            ],
          },
        },
      });

      expect(screen.getByText('Valid prompt')).toBeInTheDocument();
      expect(screen.queryByText('Number label')).not.toBeInTheDocument();
      expect(screen.queryByText('Missing prompt')).not.toBeInTheDocument();
      expect(screen.queryByText('Empty label')).not.toBeInTheDocument();
    });

    it('filters out non-object entries from JSON array', async () => {
      const badJson = JSON.stringify([
        'just a string',
        null,
        42,
        { label: 'Real prompt', prompt: 'Real prompt text' },
      ]);

      await renderCopilotPanel({
        preloadedState: {
          settings: {
            settings: [
              { settingKey: 15, settingValue: badJson },
            ],
          },
        },
      });

      expect(screen.getByText('Real prompt')).toBeInTheDocument();
    });

    it('does not render prompts when JSON is a non-array value', async () => {
      const { defaultStrings } = await loadModules();
      await renderCopilotPanel({
        preloadedState: {
          settings: {
            settings: [
              { settingKey: 15, settingValue: JSON.stringify({ label: 'Not array', prompt: 'Not array' }) },
            ],
          },
        },
      });

      expect(screen.queryByText(defaultStrings.Copilot.tryOneOfTheseToGetStarted)).not.toBeInTheDocument();
      expect(screen.queryByText('Not array')).not.toBeInTheDocument();
    });

    it('disables prompt buttons when loading', async () => {
      await renderCopilotPanel({
        preloadedState: {
          copilot: { isLoading: true },
          settings: {
            settings: [
              { settingKey: 15, settingValue: validPromptsJson },
            ],
          },
        },
      });

      const buttons = screen.getAllByRole('button', { name: /Include/i });
      expect(buttons).toHaveLength(2);
      for (const button of buttons) {
        expect(button).toBeDisabled();
      }
    });
  });
});