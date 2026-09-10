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
  selectLastAppliedOperations,
  selectUseOrgStructure,
  selectIsPanelOpen,
  mergeResultingParts,
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
  lastAppliedOperations: [],
  useOrgStructure: false,
  warning: null,
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
        lastAppliedOperations: [{ op: 'add', partId: 'p1' }],
        useOrgStructure: true,
      };
      const state = copilotReducer(populated, clearLastSourcePart());
      expect(state.lastSourceParts).toEqual([]);
      expect(state.lastAppliedOperations).toEqual([]);
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

    it('fulfilled stores appliedOperations from the response payload', () => {
      const payload = {
        message: mockAssistantMessage,
        sourceParts: [mockSourcePart],
        useOrgStructure: false,
        appliedOperations: [{ op: 'replace', partId: 'p1' }],
      };
      const state = copilotReducer(initialState, {
        type: sendCopilotMessage.fulfilled.type,
        payload,
      });
      expect(state.lastAppliedOperations).toEqual([{ op: 'replace', partId: 'p1' }]);
    });

    it('fulfilled with no applied operations (no-op turn) leaves appliedOperations empty', () => {
      // Describe/clarify/refuse turns return the query unchanged with an empty operation set.
      // The empty list is what gates the Accept & Apply button in the panel.
      const payload = {
        message: mockAssistantMessage,
        sourceParts: [mockSourcePart],
        useOrgStructure: false,
        appliedOperations: [],
      };
      const state = copilotReducer(initialState, {
        type: sendCopilotMessage.fulfilled.type,
        payload,
      });
      expect(state.lastSourceParts).toEqual([mockSourcePart]);
      expect(state.lastAppliedOperations).toEqual([]);
    });

    it('fulfilled without appliedOperations defaults to empty', () => {
      const payload = {
        message: mockAssistantMessage,
        sourceParts: [mockSourcePart],
        useOrgStructure: false,
      };
      const state = copilotReducer(initialState, {
        type: sendCopilotMessage.fulfilled.type,
        payload,
      });
      expect(state.lastAppliedOperations).toEqual([]);
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
        lastAppliedOperations: [{ op: 'add', partId: 'p1' }],
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

    it('selectLastAppliedOperations returns applied operations', () => {
      expect(selectLastAppliedOperations(mockRootState)).toEqual([{ op: 'add', partId: 'p1' }]);
    });

    it('selectUseOrgStructure returns flag', () => {
      expect(selectUseOrgStructure(mockRootState)).toBe(true);
    });

    it('selectIsPanelOpen returns panel state', () => {
      expect(selectIsPanelOpen(mockRootState)).toBe(true);
    });
  });

  // T017 [US4]: apply the resulting query BY partId
  describe('mergeResultingParts (apply-by-id)', () => {
    const hr = (id: string, filter: string, extra?: Partial<ISourcePart>): ISourcePart => ({
      id,
      title: filter,
      query: { type: SourcePartType.HR, source: { filter }, exclusionary: false },
      isNew: false,
      isExpanded: false,
      ...extra,
    });

    it('replaces a matching id in place and preserves its expanded state', () => {
      const existing = [hr('a', 'A = 1', { isExpanded: true })];
      const resulting = [hr('a', 'A = 99')];

      const merged = mergeResultingParts(existing, resulting);

      expect(merged).toHaveLength(1);
      expect(merged[0].id).toBe('a');
      expect((merged[0].query as { source: { filter: string } }).source.filter).toBe('A = 99');
      expect(merged[0].isExpanded).toBe(true); // preserved from existing
      expect(merged[0].isNew).toBe(false); // existing parts are not forced to isNew
    });

    it('drops ids that are absent from the resulting query', () => {
      const existing = [hr('a', 'A = 1'), hr('b', 'B = 2')];
      const resulting = [hr('a', 'A = 1')];

      const merged = mergeResultingParts(existing, resulting);

      expect(merged.map(p => p.id)).toEqual(['a']);
    });

    it('appends server-minted parts and keeps their isNew flag', () => {
      const existing = [hr('a', 'A = 1')];
      const resulting = [hr('a', 'A = 1'), hr('minted-1', 'C = 3', { isNew: true })];

      const merged = mergeResultingParts(existing, resulting);

      expect(merged.map(p => p.id)).toEqual(['a', 'minted-1']);
      expect(merged[1].isNew).toBe(true);
    });

    it('does not force isNew:true on parts that already existed', () => {
      const existing = [hr('a', 'A = 1', { isNew: false }), hr('b', 'B = 2', { isNew: false })];
      const resulting = [hr('b', 'B = 2'), hr('a', 'A = 1')]; // reordered, both pre-existing

      const merged = mergeResultingParts(existing, resulting);

      expect(merged.every(p => p.isNew === false)).toBe(true);
      expect(merged.map(p => p.id)).toEqual(['b', 'a']); // resulting order is authoritative
    });

    it('carries forward a resolved org leader when the refined part comes back without one', () => {
      const existing: ISourcePart[] = [
        {
          id: 'a',
          title: 'A = 1',
          query: { type: SourcePartType.HR, source: { filter: 'A = 1', manager: { id: 4242, depth: 2 } }, exclusionary: false },
          isNew: false,
          isExpanded: false,
          useOrgStructure: true,
        },
      ];
      // Server returned a filter-only refine of the same part: still org-structure, no manager present.
      const resulting: ISourcePart[] = [
        {
          id: 'a',
          title: 'A = 1 AND B = 2',
          query: { type: SourcePartType.HR, source: { filter: 'A = 1 AND B = 2' }, exclusionary: false },
          isNew: false,
          isExpanded: false,
          useOrgStructure: true,
        },
      ];

      const merged = mergeResultingParts(existing, resulting);

      const source = (merged[0].query as { source: { filter: string; manager?: { id?: number; depth?: number } } }).source;
      expect(source.filter).toBe('A = 1 AND B = 2'); // refined filter wins
      expect(source.manager?.id).toBe(4242); // resolved leader preserved
      expect(source.manager?.depth).toBe(2);
    });

    it('does not overwrite a newly resolved org leader on the refined part', () => {
      const existing: ISourcePart[] = [
        {
          id: 'a',
          title: 'A = 1',
          query: { type: SourcePartType.HR, source: { filter: 'A = 1', manager: { id: 1111, depth: 1 } }, exclusionary: false },
          isNew: false,
          isExpanded: false,
          useOrgStructure: true,
        },
      ];
      const resulting: ISourcePart[] = [
        {
          id: 'a',
          title: 'A = 1',
          query: { type: SourcePartType.HR, source: { filter: 'A = 1', manager: { id: 9999, depth: 3 } }, exclusionary: false },
          isNew: false,
          isExpanded: false,
          useOrgStructure: true,
        },
      ];

      const merged = mergeResultingParts(existing, resulting);

      const source = (merged[0].query as { source: { manager?: { id?: number } } }).source;
      expect(source.manager?.id).toBe(9999); // new leader is authoritative, not carried-over
    });

    it('does NOT resurrect an org leader that the refine intentionally removed', () => {
      const existing: ISourcePart[] = [
        {
          id: 'a',
          title: 'A = 1',
          query: { type: SourcePartType.HR, source: { filter: 'A = 1', manager: { id: 4242, depth: 2 } }, exclusionary: false },
          isNew: false,
          isExpanded: false,
          useOrgStructure: true,
        },
      ];
      // Refine turned org structure OFF for this part — the leader must not come back.
      const resulting: ISourcePart[] = [
        {
          id: 'a',
          title: 'A = 1',
          query: { type: SourcePartType.HR, source: { filter: 'A = 1' }, exclusionary: false },
          isNew: false,
          isExpanded: false,
          useOrgStructure: false,
        },
      ];

      const merged = mergeResultingParts(existing, resulting);

      const source = (merged[0].query as { source: { manager?: { id?: number } } }).source;
      expect(source.manager?.id).toBeUndefined();
    });

    it('does NOT overwrite a newly named (but unresolved) org leader with the stale one', () => {
      const existing: ISourcePart[] = [
        {
          id: 'a',
          title: 'A = 1',
          query: { type: SourcePartType.HR, source: { filter: 'A = 1', manager: { id: 4242, depth: 2 } }, exclusionary: false },
          isNew: false,
          isExpanded: false,
          useOrgStructure: true,
        },
      ];
      // Refine named a different leader that did not resolve to an employeeId this turn: manager.id is
      // absent, but managerToAutoSelect carries the new intent — the old leader must not be carried over.
      const resulting: ISourcePart[] = [
        {
          id: 'a',
          title: 'A = 1',
          query: { type: SourcePartType.HR, source: { filter: 'A = 1' }, exclusionary: false },
          isNew: false,
          isExpanded: false,
          useOrgStructure: true,
          managerToAutoSelect: { displayName: 'New Leader' },
        },
      ];

      const merged = mergeResultingParts(existing, resulting);

      const source = (merged[0].query as { source: { manager?: { id?: number } } }).source;
      expect(source.manager?.id).toBeUndefined();
    });
  });
});
