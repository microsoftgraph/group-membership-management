// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, it, expect } from 'vitest';
import copilotReducer, {
  addMessage,
  clearMessages,
  setError,
  clearLastSourcePart,
  openPanel,
  closePanel,
  selectCopilotMessages,
  selectCopilotIsLoading,
  selectCopilotError,
  selectLastSourceParts,
  selectUseOrgStructure,
  selectIsPanelOpen,
  CopilotState,
} from './copilot.slice';
import { sendCopilotMessage } from './copilot.api';
import { IChatMessage } from '../components/CopilotPanel/CopilotPanel.types';
import { ISourcePart } from '../models/ISourcePart';
import { SourcePartType } from '../models/SourcePartType';
import { RootState } from './store';

const initialState: CopilotState = {
  messages: [],
  isLoading: false,
  error: null,
  isPanelOpen: false,
  lastSourceParts: [],
  useOrgStructure: false,
  conversationId: 'test-conversation-id',
};

const mockMessage: IChatMessage = {
  id: 'msg-1',
  role: 'user',
  content: 'Include all FTEs',
  timestamp: '2026-04-19T00:00:00.000Z',
};

const mockAssistantMessage: IChatMessage = {
  id: 'msg-2',
  role: 'assistant',
  content: 'Here is your filter.',
  timestamp: '2026-04-19T00:00:01.000Z',
};

const mockSourcePart: ISourcePart = {
  id: 'sp-1',
  title: 'FTEs',
  query: { type: SourcePartType.HR, source: { filter: "EmployeeType_Code = 'FTE'" }, exclusionary: false },
  isNew: true,
  isExpanded: false,
  useOrgStructure: false,
};

describe('copilot.slice', () => {
  describe('reducers', () => {
    it('should return the initial state', () => {
      const state = copilotReducer(undefined, { type: 'unknown' });
      expect(state.messages).toEqual([]);
      expect(state.isLoading).toBe(false);
      expect(state.error).toBeNull();
      expect(state.isPanelOpen).toBe(false);
      expect(state.lastSourceParts).toEqual([]);
      expect(state.useOrgStructure).toBe(false);
      expect(state.conversationId).toBeDefined();
      expect(state.conversationId.length).toBeGreaterThan(0);
    });

    it('addMessage should add a message and clear error', () => {
      const stateWithError = { ...initialState, error: 'previous error' };
      const state = copilotReducer(stateWithError, addMessage(mockMessage));
      expect(state.messages).toHaveLength(1);
      expect(state.messages[0]).toEqual(mockMessage);
      expect(state.error).toBeNull();
    });

    it('addMessage should append to existing messages', () => {
      const stateWithMsg = { ...initialState, messages: [mockMessage] };
      const state = copilotReducer(stateWithMsg, addMessage(mockAssistantMessage));
      expect(state.messages).toHaveLength(2);
      expect(state.messages[1].role).toBe('assistant');
    });

    it('clearMessages should reset messages, error, sourceParts, and useOrgStructure', () => {
      const populated: CopilotState = {
        ...initialState,
        messages: [mockMessage],
        error: 'some error',
        lastSourceParts: [mockSourcePart],
        useOrgStructure: true,
      };
      const state = copilotReducer(populated, clearMessages());
      expect(state.messages).toEqual([]);
      expect(state.error).toBeNull();
      expect(state.lastSourceParts).toEqual([]);
      expect(state.useOrgStructure).toBe(false);
    });

    it('setError should set the error message', () => {
      const state = copilotReducer(initialState, setError('Something failed'));
      expect(state.error).toBe('Something failed');
    });

    it('setError with null should clear the error', () => {
      const stateWithError = { ...initialState, error: 'old error' };
      const state = copilotReducer(stateWithError, setError(null));
      expect(state.error).toBeNull();
    });

    it('clearLastSourcePart should clear source parts and useOrgStructure', () => {
      const populated: CopilotState = {
        ...initialState,
        lastSourceParts: [mockSourcePart],
        useOrgStructure: true,
      };
      const state = copilotReducer(populated, clearLastSourcePart());
      expect(state.lastSourceParts).toEqual([]);
      expect(state.useOrgStructure).toBe(false);
    });

    it('openPanel should set isPanelOpen to true', () => {
      const state = copilotReducer(initialState, openPanel());
      expect(state.isPanelOpen).toBe(true);
    });

    it('closePanel should set isPanelOpen to false', () => {
      const openState = { ...initialState, isPanelOpen: true };
      const state = copilotReducer(openState, closePanel());
      expect(state.isPanelOpen).toBe(false);
    });
  });

  describe('extraReducers (sendCopilotMessage)', () => {
    it('pending should set isLoading and clear error', () => {
      const stateWithError = { ...initialState, error: 'old' };
      const state = copilotReducer(stateWithError, { type: sendCopilotMessage.pending.type });
      expect(state.isLoading).toBe(true);
      expect(state.error).toBeNull();
    });

    it('fulfilled should add message, set sourceParts and useOrgStructure', () => {
      const loadingState = { ...initialState, isLoading: true };
      const payload = {
        message: mockAssistantMessage,
        sourceParts: [mockSourcePart],
        useOrgStructure: false,
      };
      const state = copilotReducer(loadingState, {
        type: sendCopilotMessage.fulfilled.type,
        payload,
      });
      expect(state.isLoading).toBe(false);
      expect(state.messages).toHaveLength(1);
      expect(state.messages[0].content).toBe('Here is your filter.');
      expect(state.lastSourceParts).toEqual([mockSourcePart]);
      expect(state.useOrgStructure).toBe(false);
    });

    it('fulfilled with useOrgStructure true should set flag', () => {
      const orgPart = { ...mockSourcePart, useOrgStructure: true };
      const payload = {
        message: mockAssistantMessage,
        sourceParts: [orgPart],
        useOrgStructure: true,
      };
      const state = copilotReducer(initialState, {
        type: sendCopilotMessage.fulfilled.type,
        payload,
      });
      expect(state.useOrgStructure).toBe(true);
    });

    it('fulfilled preserves createdViaAIQB tag on transformed source parts', () => {
      // Copilot-produced source parts must carry createdViaAIQB: true so the
      // submit-time computation of newJob.onboardedUsingAIQB can detect AIQB retention.
      const aiqbPart: ISourcePart = { ...mockSourcePart, createdViaAIQB: true };
      const payload = {
        message: mockAssistantMessage,
        sourceParts: [aiqbPart],
        useOrgStructure: false,
      };
      const state = copilotReducer(initialState, {
        type: sendCopilotMessage.fulfilled.type,
        payload,
      });
      expect(state.lastSourceParts).toHaveLength(1);
      expect(state.lastSourceParts[0].createdViaAIQB).toBe(true);
    });

    it('rejected should set error and stop loading', () => {
      const loadingState = { ...initialState, isLoading: true };
      const state = copilotReducer(loadingState, {
        type: sendCopilotMessage.rejected.type,
        error: { message: 'Network error' },
      });
      expect(state.isLoading).toBe(false);
      expect(state.error).toBe('Network error');
    });

    it('rejected without message should use fallback error', () => {
      const state = copilotReducer(initialState, {
        type: sendCopilotMessage.rejected.type,
        error: {},
      });
      expect(state.error).toBe('An error occurred');
    });
  });

  describe('selectors', () => {
    const mockRootState = {
      copilot: {
        messages: [mockMessage],
        isLoading: true,
        error: 'test error',
        isPanelOpen: true,
        lastSourceParts: [mockSourcePart],
        useOrgStructure: true,
      },
    } as unknown as RootState;

    it('selectCopilotMessages returns messages', () => {
      expect(selectCopilotMessages(mockRootState)).toEqual([mockMessage]);
    });

    it('selectCopilotIsLoading returns loading state', () => {
      expect(selectCopilotIsLoading(mockRootState)).toBe(true);
    });

    it('selectCopilotError returns error', () => {
      expect(selectCopilotError(mockRootState)).toBe('test error');
    });

    it('selectLastSourceParts returns source parts', () => {
      expect(selectLastSourceParts(mockRootState)).toEqual([mockSourcePart]);
    });

    it('selectUseOrgStructure returns flag', () => {
      expect(selectUseOrgStructure(mockRootState)).toBe(true);
    });

    it('selectIsPanelOpen returns panel state', () => {
      expect(selectIsPanelOpen(mockRootState)).toBe(true);
    });
  });
});
