// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { initializeIcons } from '@fluentui/react';
import { beforeAll, describe, expect, it, vi } from 'vitest';

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
    type: 'SqlMembership',
    source: {
      filter: "EmployeeType_Code = 'FTE'",
    },
    exclusionary: false,
  },
  isNew: true,
  isExpanded: false,
  useOrgStructure: false,
};

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
}: {
  isOpen?: boolean;
  preloadedState?: Record<string, any>;
  dismissPanel?: () => void;
} = {}) => {
  const { renderWithProviders, CopilotPanel } = await loadModules();
  const result = renderWithProviders(
    <CopilotPanel isOpen={isOpen} dismissPanel={dismissPanel} />,
    {
      preloadedState: await buildPreloadedState(preloadedState),
    }
  );

  return {
    dismissPanel,
    ...result,
  };
};

describe('CopilotPanel', () => {
  it('does not render panel content when isOpen is false', async () => {
    const { defaultStrings } = await loadModules();
    await renderCopilotPanel({ isOpen: false });

    expect(screen.queryByText(defaultStrings.Copilot.welcomeMessage)).not.toBeInTheDocument();
  });

  it('renders welcome message with steps when open and no messages', async () => {
    const { defaultStrings } = await loadModules();
    await renderCopilotPanel();

    expect(screen.getByText(defaultStrings.Copilot.welcomeMessage)).toBeInTheDocument();
    expect(screen.getByText(defaultStrings.Copilot.welcomeStep1Bold)).toBeInTheDocument();
    expect(screen.getByText(defaultStrings.Copilot.welcomeStep2Bold)).toBeInTheDocument();
    expect(screen.getByText(defaultStrings.Copilot.welcomeStep3Bold)).toBeInTheDocument();
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
          error: 'Something went wrong',
        },
      },
    });

    expect(screen.getByText(defaultStrings.Copilot.errorMessage)).toBeInTheDocument();
  });

  it('shows Accept & Apply button when lastSourceParts exist', async () => {
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
        },
      },
    });

    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.resumeDialogContinue }));

    expect(
      await screen.findByRole('button', { name: defaultStrings.Copilot.acceptAndApply })
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

    fireEvent.click(screen.getByRole('button', { name: defaultStrings.Copilot.resumeDialogContinue }));

    fireEvent.click(
      await screen.findByRole('button', { name: defaultStrings.Copilot.newConversation })
    );

    expect(store.getState().copilot.messages).toEqual([]);
    expect(store.getState().copilot.error).toBeNull();
    expect(store.getState().copilot.lastSourceParts).toEqual([]);
    expect(store.getState().copilot.useOrgStructure).toBe(false);
  });

  describe('suggested prompts', () => {
    const validPromptsJson = JSON.stringify([
      { label: 'Include reports', prompt: 'Include all reports who roll up to an employee' },
      { label: 'Include group members', prompt: 'Include all members of a specific group' },
    ]);

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

    it('renders localized header when suggested prompts are present', async () => {
      const { defaultStrings } = await loadModules();
      await renderCopilotPanel({
        preloadedState: {
          settings: {
            settings: [
              { settingKey: 15, settingValue: validPromptsJson },
            ],
          },
        },
      });

      expect(
        screen.getByText(defaultStrings.Copilot.tryOneOfTheseToGetStarted)
      ).toBeInTheDocument();
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

    it('does not render suggested prompts when setting is missing', async () => {
      const { defaultStrings } = await loadModules();
      await renderCopilotPanel();

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
