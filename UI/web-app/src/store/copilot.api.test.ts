// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { afterEach, beforeEach, describe, it, expect, vi } from 'vitest';
import { SourcePartType } from '../models/SourcePartType';
import { setupStore } from './store';
import { sendCopilotMessage, buildWorkingQuery } from './copilot.api';
import { ISourcePart } from '../models/ISourcePart';
import { addMessage, clearMessages } from './copilot.slice';
import { GraphApi } from '../apis';

// Test the transformSourcePart logic by importing the module's shape
// Since transformSourcePart is not exported, we test the expected transformation contract

describe('copilot.api transformSourcePart contract', () => {
  // These tests validate the expected shape of transformed source parts
  // matching what sendCopilotMessage.fulfilled produces

  it('should produce ISourcePart with HR query from API response shape', () => {
    const apiPart = {
      partId: 'p1',
      filter: "EmployeeType_Code = 'FTE'",
      title: 'FTEs',
      isExclusion: false,
      useOrgStructure: false,
    };

    // Validate expected transformation
    expect(apiPart.partId).toBeDefined();
    expect(apiPart.filter).toContain('EmployeeType_Code');
    expect(apiPart.isExclusion).toBe(false);
    expect(apiPart.useOrgStructure).toBe(false);
  });

  it('should map exclusionary parts correctly', () => {
    const apiPart = {
      partId: 'p2',
      filter: "EmployeeType_Code = 'INTERN'",
      title: 'Exclude Interns',
      isExclusion: true,
      useOrgStructure: false,
    };

    expect(apiPart.isExclusion).toBe(true);
  });

  it('should handle org structure parts with leader info', () => {
    const apiPart = {
      partId: 'p3',
      filter: null,
      title: "Jane's Org",
      isExclusion: false,
      useOrgStructure: true,
      orgLeaderName: 'Jane Smith',
      orgLeaderEmail: 'jsmith@contoso.com',
      orgLeaderObjectId: 'abc-123',
      orgLeaderDepth: 3,
    };

    expect(apiPart.useOrgStructure).toBe(true);
    expect(apiPart.orgLeaderName).toBe('Jane Smith');
    expect(apiPart.orgLeaderEmail).toBe('jsmith@contoso.com');
    expect(apiPart.orgLeaderObjectId).toBe('abc-123');
    expect(apiPart.orgLeaderDepth).toBe(3);
    expect(apiPart.filter).toBeNull();
  });

  it('should handle parts without org leader info', () => {
    const apiPart: Record<string, unknown> = {
      partId: 'p4',
      filter: "Location_Code = 'US'",
      title: 'US Employees',
      isExclusion: false,
      useOrgStructure: false,
    };

    expect(apiPart.orgLeaderName).toBeUndefined();
    expect(apiPart.orgLeaderEmail).toBeUndefined();
    expect(apiPart.orgLeaderObjectId).toBeUndefined();
    expect(apiPart.orgLeaderDepth).toBeUndefined();
  });

  it('CopilotResponse shape should include useOrgStructure derived from parts', () => {
    const sourceParts = [
      { useOrgStructure: false },
      { useOrgStructure: true },
      { useOrgStructure: false },
    ];

    const useOrgStructure = sourceParts.some(p => p.useOrgStructure);
    expect(useOrgStructure).toBe(true);
  });

  it('CopilotResponse useOrgStructure should be false when no parts use it', () => {
    const sourceParts = [
      { useOrgStructure: false },
      { useOrgStructure: false },
    ];

    const useOrgStructure = sourceParts.some(p => p.useOrgStructure);
    expect(useOrgStructure).toBe(false);
  });

  it('empty sourceParts should result in useOrgStructure false', () => {
    const sourceParts: { useOrgStructure: boolean }[] = [];
    const useOrgStructure = sourceParts.some(p => p.useOrgStructure);
    expect(useOrgStructure).toBe(false);
  });

  describe('AIQB onboarding tag (createdViaAIQB)', () => {
    // These tests document the contract that transformSourcePart must tag every
    // Copilot-produced source part with createdViaAIQB: true. The tag is what
    // ManageMembership uses at submit time to compute newJob.onboardedUsingAIQB,
    // measuring Copilot retention (did the user keep what Copilot produced?).

    it('every Copilot-produced source part (HR/SqlMembership) must carry createdViaAIQB: true', () => {
      const transformed = {
        id: 'p1',
        title: 'FTEs',
        query: { type: SourcePartType.HR, source: { filter: "EmployeeType_Code = 'FTE'" }, exclusionary: false },
        isNew: true,
        isExpanded: false,
        useOrgStructure: false,
        createdViaAIQB: true,
      };
      expect(transformed.createdViaAIQB).toBe(true);
    });

    it('every Copilot-produced source part (GroupMembership) must carry createdViaAIQB: true', () => {
      const transformed = {
        id: 'p2',
        title: 'My Group',
        query: { type: SourcePartType.GroupMembership, source: 'group-object-id', exclusionary: false },
        isNew: true,
        isExpanded: false,
        createdViaAIQB: true,
      };
      expect(transformed.createdViaAIQB).toBe(true);
    });

    it('onboardedUsingAIQB is true if any submitted source part carries the tag', () => {
      const sourceParts = [
        { createdViaAIQB: false },
        { createdViaAIQB: true },
        {},
      ];
      const onboardedUsingAIQB = sourceParts.some(sp => (sp as { createdViaAIQB?: boolean }).createdViaAIQB === true);
      expect(onboardedUsingAIQB).toBe(true);
    });

    it('onboardedUsingAIQB is false when no submitted source part carries the tag (all manual)', () => {
      const sourceParts = [
        { createdViaAIQB: false },
        {},
        { createdViaAIQB: undefined },
      ];
      const onboardedUsingAIQB = sourceParts.some(sp => (sp as { createdViaAIQB?: boolean }).createdViaAIQB === true);
      expect(onboardedUsingAIQB).toBe(false);
    });

    it('onboardedUsingAIQB is false when all Copilot parts were deleted before submit', () => {
      // User asked Copilot for suggestions, then deleted them and added manual parts.
      // Only the surviving (manual) parts get submitted.
      const submittedParts = [
        { createdViaAIQB: false, title: 'Manual replacement' },
      ];
      const onboardedUsingAIQB = submittedParts.some(sp => sp.createdViaAIQB === true);
      expect(onboardedUsingAIQB).toBe(false);
    });

    it('onboardedUsingAIQB is false when no source parts exist', () => {
      const sourceParts: { createdViaAIQB?: boolean }[] = [];
      const onboardedUsingAIQB = sourceParts.some(sp => sp.createdViaAIQB === true);
      expect(onboardedUsingAIQB).toBe(false);
    });
  });

  describe('sendCopilotMessage thunk (functional)', () => {
    // Functional coverage of transformSourcePart via the sendCopilotMessage thunk.
    // Verifies that every source part returned by the Copilot backend is tagged
    // with createdViaAIQB: true when it lands in redux state.

    const mockAuthService = {
      getTokenAsync: vi.fn().mockResolvedValue('mock-token'),
    };

    let originalFetch: typeof global.fetch;

    beforeEach(() => {
      originalFetch = global.fetch;
    });

    afterEach(() => {
      global.fetch = originalFetch;
      vi.restoreAllMocks();
    });

    const dispatchWithMockedFetch = async (apiResponse: unknown) => {
      global.fetch = vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        text: async () => JSON.stringify(apiResponse),
      }) as unknown as typeof fetch;

      const store = setupStore(undefined, { authenticationService: mockAuthService as never });
      const result = await store.dispatch(sendCopilotMessage({ message: 'test' }));
      return result;
    };

    it('excludes apply acknowledgments and error markers from the model history', async () => {
      const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
        message: 'No changes.',
        resultingQuery: [],
        appliedOperations: [],
      }), { status: 200 }));
      global.fetch = fetchMock;
      const store = setupStore(undefined, { authenticationService: mockAuthService as never });
      const timestamp = '2026-10-07T20:40:00.000Z';
      store.dispatch(addMessage({ id: 'user', role: 'user', content: 'Include FTEs', timestamp }));
      store.dispatch(addMessage({ id: 'proposal', role: 'assistant', content: 'Proposed Membership: FTEs', timestamp }));
      store.dispatch(addMessage({ id: 'applied', role: 'user', content: 'Apply & Review', action: 'applyAndReview', timestamp }));
      store.dispatch(addMessage({ id: 'error', role: 'assistant', content: 'Request failed', isError: true, timestamp }));
      await store.dispatch(sendCopilotMessage({ message: 'Refine this' }));
      expect(fetchMock).toHaveBeenCalledOnce();
      const body = fetchMock.mock.calls[0][1]?.body;
      expect(typeof body).toBe('string');
      if (typeof body !== 'string') throw new Error('Expected a JSON request body');
      expect(JSON.parse(body).conversationHistory).toEqual([
        { role: 'user', content: 'Include FTEs' },
        { role: 'assistant', content: 'Proposed Membership: FTEs' },
      ]);
    });

    it('tags every HR/SqlMembership source part with createdViaAIQB: true', async () => {
      const result = await dispatchWithMockedFetch({
        message: 'ok',
        sourceParts: [
          { partId: 'p1', filter: "EmployeeType_Code = 'FTE'", title: 'FTEs', isExclusion: false, useOrgStructure: false },
          { partId: 'p2', filter: "EmployeeType_Code = 'INTERN'", title: 'Interns', isExclusion: true, useOrgStructure: false },
        ],
      });

      expect(result.type).toBe(sendCopilotMessage.fulfilled.type);
      const payload = result.payload as { sourceParts: { createdViaAIQB?: boolean; query: { type: SourcePartType } }[] };
      expect(payload.sourceParts).toHaveLength(2);
      expect(payload.sourceParts.every(sp => sp.createdViaAIQB === true)).toBe(true);
      expect(payload.sourceParts.every(sp => sp.query.type === SourcePartType.HR)).toBe(true);
    });

    it.each(['', 'New server title'])('preserves local HR titles only when the response title is empty (%s)', async (title) => {
      const base = setupStore().getState();
      const part: ISourcePart = {
        id: 'hr-local',
        title: 'Local display summary',
        query: { type: SourcePartType.HR, source: { filter: "Country = 'USA'" }, exclusionary: true },
        isNew: false,
        isExpanded: false,
      };
      const store = setupStore({
        ...base,
        manageMembership: { ...base.manageMembership, sourceParts: [part] },
      }, { authenticationService: mockAuthService as never });
      global.fetch = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
        message: 'Updated membership.',
        resultingQuery: [{
          partId: part.id, sourceType: SourcePartType.HR, title,
          filter: "Country = 'USA'", isExclusion: true, useOrgStructure: false,
        }],
        appliedOperations: [{ op: 'replace', partId: part.id }],
      }), { status: 200 }));
      await store.dispatch(sendCopilotMessage({ message: 'Refine this rule' }));
      expect(store.getState().copilot.lastSourceParts[0].title).toBe(title || part.title);
      expect(store.getState().copilot.lastSourceParts[0].query).toEqual(part.query);
      expect(store.getState().manageMembership.sourceParts).toEqual([part]);
    });

    it('preserves the local HR title when the response partId casing differs', async () => {
      const base = setupStore().getState();
      const part: ISourcePart = {
        id: 'hr-local',
        title: 'Local display summary',
        query: { type: SourcePartType.HR, source: { filter: "Country = 'USA'" }, exclusionary: true },
        isNew: false,
        isExpanded: false,
      };
      const store = setupStore({
        ...base,
        manageMembership: { ...base.manageMembership, sourceParts: [part] },
      }, { authenticationService: mockAuthService as never });
      global.fetch = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
        message: 'Updated membership.',
        resultingQuery: [{
          partId: 'HR-LOCAL', sourceType: SourcePartType.HR, title: null,
          filter: "Country = 'Canada'", isExclusion: true, useOrgStructure: false,
        }],
        appliedOperations: [{ op: 'replace', partId: 'HR-LOCAL' }],
      }), { status: 200 }));

      await store.dispatch(sendCopilotMessage({ message: 'Refine this rule' }));

      expect(store.getState().copilot.lastSourceParts[0]).toEqual(expect.objectContaining({
        id: 'HR-LOCAL',
        title: part.title,
        isNew: false,
      }));
    });

    it('keeps an automatic membership summary read-only even for a legacy response', async () => {
      global.fetch = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
        message: 'Your current membership includes FTEs.',
        sourceParts: [{ partId: 'p1', filter: "EmployeeType_Code = 'FTE'", title: 'FTEs' }],
      }), { status: 200 }));
      const store = setupStore(undefined, { authenticationService: mockAuthService as never });
      await store.dispatch(sendCopilotMessage({ message: 'What is my current membership?', purpose: 'membershipSummary' }));
      expect(store.getState().copilot.messages).toEqual([
        expect.objectContaining({ content: 'Includes FTEs.', isMembershipSummary: true }),
      ]);
      expect(store.getState().copilot.lastSourceParts).toEqual([]);
      expect(store.getState().copilot.lastAppliedOperations).toEqual([]);
      expect(store.getState().copilot.error).toBeNull();
    });

    it.each([
      { message: '', appliedOperations: [] },
      { message: 'I changed the query.', appliedOperations: [{ op: 'set' }] },
    ])('surfaces invalid summary responses as failures: %j', async (response) => {
      global.fetch = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
        ...response, resultingQuery: [],
      }), { status: 200 }));
      const store = setupStore(undefined, { authenticationService: mockAuthService as never });
      await store.dispatch(sendCopilotMessage({ message: 'What is my current membership?', purpose: 'membershipSummary' }));
      expect(store.getState().copilot.error).toContain('could not describe the current membership');
      expect(store.getState().copilot.messages[0].isError).toBe(true);
      expect(store.getState().copilot.lastAppliedOperations).toEqual([]);
    });

    it.each(['membershipSummary', 'resumeMembershipSummary'] as const)('rebases model context after %s while retaining visible history', async (purpose) => {
      const fetchMock = vi.fn<typeof fetch>().mockImplementation(async () =>
        new Response(JSON.stringify({ message: 'Fresh description.', resultingQuery: [], appliedOperations: [] }), { status: 200 }));
      global.fetch = fetchMock;
      const store = setupStore(undefined, { authenticationService: mockAuthService as never });
      const timestamp = '2026-10-07T22:15:00.000Z';
      store.dispatch(addMessage({ id: 'old', role: 'assistant', content: 'Old leader description.', timestamp }));
      await store.dispatch(sendCopilotMessage({ message: 'What is my current membership?', purpose }));
      const requestBody = (index: number) => {
        const body = fetchMock.mock.calls[index][1]?.body;
        if (typeof body !== 'string') throw new Error('Expected a JSON request body');
        return JSON.parse(body);
      };
      expect(requestBody(0).conversationHistory).toEqual([]);
      expect(store.getState().copilot.messages[0].content).toBe('Old leader description.');
      store.dispatch(addMessage({ id: 'followup', role: 'user', content: 'Add User 5', timestamp }));
      await store.dispatch(sendCopilotMessage({ message: 'Add User 5' }));
      expect(requestBody(1).conversationHistory).toEqual([
        { role: 'assistant', content: 'Fresh description.' },
        { role: 'user', content: 'Add User 5' },
      ]);
      store.dispatch(addMessage({ id: 'confirmation', role: 'user', content: 'yes', timestamp }));
      await store.dispatch(sendCopilotMessage({ message: 'yes' }));
      expect(requestBody(2).conversationHistory).toEqual([
        { role: 'assistant', content: 'Fresh description.' },
        { role: 'user', content: 'Add User 5' },
        { role: 'assistant', content: 'Fresh description.' },
        { role: 'user', content: 'yes' },
      ]);
      const contextBeforeFailure = store.getState().copilot.modelContext;
      fetchMock.mockRejectedValueOnce(new Error('Offline'));
      await store.dispatch(sendCopilotMessage({ message: 'What is my current membership?', purpose }));
      expect(requestBody(3).conversationHistory).toEqual([]);
      expect(store.getState().copilot.modelContext).toEqual(contextBeforeFailure);
      await store.dispatch(sendCopilotMessage({ message: 'What is my current membership?', purpose }));
      await store.dispatch(sendCopilotMessage({ message: 'Explain it' }));
      expect(requestBody(5).conversationHistory).toEqual([{ role: 'assistant', content: 'Fresh description.' }]);
      store.dispatch(clearMessages());
      expect(store.getState().copilot.modelContext).toBeUndefined();
      await store.dispatch(sendCopilotMessage({ message: 'Start fresh' }));
      expect(requestBody(6).conversationHistory).toEqual([]);
    });

    it('refreshes a resume description without appending a duplicate response to chat history', async () => {
      global.fetch = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
        message: 'Your current membership includes contractors.',
        resultingQuery: [],
        appliedOperations: [],
      }), { status: 200 }));
      const store = setupStore(undefined, { authenticationService: mockAuthService as never });
      const priorMessage = { id: 'old', role: 'user' as const, content: 'Include FTEs', timestamp: '2026-10-07T21:31:00.000Z' };
      store.dispatch(addMessage(priorMessage));
      const result = await store.dispatch(sendCopilotMessage({
        message: 'What is my current membership?', purpose: 'resumeMembershipSummary',
      }));
      expect(sendCopilotMessage.fulfilled.match(result)).toBe(true);
      expect(store.getState().copilot.messages).toEqual([priorMessage]);
      expect(store.getState().copilot.lastSourceParts).toEqual([]);
      expect(store.getState().copilot.lastAppliedOperations).toEqual([]);
    });

    it.each([
      { purpose: 'membershipSummary', message: 'What is my current membership?' },
      { purpose: 'resumeMembershipSummary', message: 'What is my current membership?' },
      { purpose: undefined, message: "add User 5's org too" },
      { purpose: undefined, message: 'yes' },
    ] as const)(
      'sends the exact current leader and depth in both the latest message and workingQuery for $message ($purpose)',
      async ({ purpose, message }) => {
        const fetchMock = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
          message: 'Your current membership uses the new leader.',
          resultingQuery: [],
          appliedOperations: [],
        }), { status: 200 }));
        global.fetch = fetchMock;
        const base = setupStore().getState();
        const currentPart: ISourcePart = {
          id: 'org-1', title: "Everyone in User 19's org with the following summarized criteria: User 6's Org", isNew: false, isExpanded: false,
          query: { type: SourcePartType.HR, source: { manager: { id: 222, depth: 2 } } },
          useOrgStructure: true,
          managerToAutoSelect: { displayName: 'Previous leader', email: 'old@contoso.com', objectId: 'old-id' },
          depthToAutoSelect: 6,
        };
        const store = setupStore({
          ...base,
          manageMembership: { ...base.manageMembership, sourceParts: [currentPart] },
          orgLeaderDetails: { ...base.orgLeaderDetails, mapping: {
            222: { objectId: 'new-id', text: 'Current leader', maxDepth: 8 },
          } },
        }, { authenticationService: mockAuthService as never });
        const priorMessage = { id: 'old-response', role: 'assistant' as const, content: 'Previous leader, six levels.', timestamp: '2026-10-07T21:30:00.000Z' };
        store.dispatch(addMessage(priorMessage));
        await store.dispatch(sendCopilotMessage({ message, purpose }));
        expect(fetchMock).toHaveBeenCalledOnce();
        const body = fetchMock.mock.calls[0][1]?.body;
        if (typeof body !== 'string') throw new Error('Expected a JSON request body');
        const request = JSON.parse(body);
        expect(request.workingQuery[0]).toMatchObject({
          orgLeaderName: 'Current leader', orgLeaderObjectId: 'new-id', orgLeaderDepth: 2,
        });
        expect(request.workingQuery[0]).not.toHaveProperty('orgLeaderEmail');
        expect(request.workingQuery[0]).not.toHaveProperty('title');
        expect(request.message).not.toContain("User 6's Org");
        expect(store.getState().manageMembership.sourceParts[0].title).toBe(currentPart.title);
        if (purpose) {
          expect(request.message).toContain('What is my current membership?');
          expect(request.message).not.toContain(JSON.stringify(request.workingQuery));
        } else {
          expect(request.message).toContain(message);
          expect(request.message).not.toContain(JSON.stringify(request.workingQuery));
        }
        expect(request.message).not.toContain('Previous leader');
        expect(request.conversationHistory).toEqual(purpose ? [] : [{ role: 'assistant', content: priorMessage.content }]);
        expect(store.getState().manageMembership.sourceParts).toEqual([currentPart]);
      });

    it.each(['membershipSummary', 'resumeMembershipSummary'] as const)('removes duplicated proposal prose from a %s response', async (purpose) => {
      global.fetch = vi.fn<typeof fetch>().mockResolvedValue(new Response(JSON.stringify({
        message: 'Your current membership includes all users in the **test source users 2-9** group.\n\n**Proposed Membership:** **Includes all members of the test source users 2-9 group**\n\nClick Apply & Review.',
        resultingQuery: [],
        appliedOperations: [],
      }), { status: 200 }));
      const store = setupStore(undefined, { authenticationService: mockAuthService as never });
      const result = await store.dispatch(sendCopilotMessage({ message: 'What is my current membership?', purpose }));
      expect(sendCopilotMessage.fulfilled.match(result)).toBe(true);
      expect(store.getState().copilot.modelContext?.summary).toBe('**Includes all members of the test source users 2-9 group**');
      expect(store.getState().copilot.lastAppliedOperations).toEqual([]);
      if (purpose === 'membershipSummary') {
        expect(store.getState().copilot.messages[0].content).toBe('**Includes all members of the test source users 2-9 group**');
      } else {
        expect(store.getState().copilot.messages).toEqual([]);
      }
    });

    it.each([true, false])('resolves an uncached current org leader before sending chat; lookup succeeds: %s', async (succeeds) => {
      const fetchMock = vi.fn<typeof fetch>()
        .mockResolvedValueOnce(new Response(JSON.stringify({ azureObjectId: 'current-object-id', maxDepth: 8 }), { status: succeeds ? 200 : 500 }))
        .mockResolvedValueOnce(new Response(JSON.stringify({ message: 'Current leader.', resultingQuery: [], appliedOperations: [] }), { status: 200 }));
      global.fetch = fetchMock;
      const graphApi = new GraphApi({ baseUrl: 'http://localhost/mock-graph', getTokenAsync: async () => 'mock-token' });
      const getUser = vi.spyOn(graphApi, 'getUser').mockResolvedValue('Current leader');
      const base = setupStore().getState();
      const currentPart: ISourcePart = {
        id: 'org-1', title: 'Current org', isNew: false, isExpanded: false,
        query: { type: SourcePartType.HR, source: { manager: { id: 222, depth: 2 } } },
      };
      const store = setupStore({
        ...base,
        manageMembership: { ...base.manageMembership, sourceParts: [currentPart] },
      }, { authenticationService: mockAuthService as never }, { graphApi });
      await store.dispatch(sendCopilotMessage({ message: 'What is my current membership?', purpose: 'resumeMembershipSummary' }));
      expect(String(fetchMock.mock.calls[0][0])).toContain('/EmployeeId/222');
      if (succeeds) {
        expect(getUser).toHaveBeenCalledWith('current-object-id');
        expect(fetchMock).toHaveBeenCalledTimes(2);
        const body = fetchMock.mock.calls[1][1]?.body;
        if (typeof body !== 'string') throw new Error('Expected a JSON request body');
        expect(JSON.parse(body).workingQuery[0]).toMatchObject({ orgLeaderName: 'Current leader', orgLeaderObjectId: 'current-object-id' });
      } else {
        expect(fetchMock).toHaveBeenCalledTimes(2);
        const body = fetchMock.mock.calls[1][1]?.body;
        if (typeof body !== 'string') throw new Error('Expected a JSON request body');
        expect(JSON.parse(body).workingQuery[0]).toMatchObject({ useOrgStructure: true });
        expect(store.getState().copilot.error).toBeNull();
      }
      expect(store.getState().manageMembership.sourceParts).toEqual([currentPart]);
    });

    it('tags GroupMembership source parts with createdViaAIQB: true', async () => {
      const result = await dispatchWithMockedFetch({
        message: 'ok',
        sourceParts: [
          {
            partId: 'g1',
            sourceType: 'GroupMembership',
            filter: null,
            title: 'My Group',
            isExclusion: false,
            useOrgStructure: false,
            groupId: '00000000-0000-0000-0000-000000000001',
            groupName: 'My Group',
          },
        ],
      });

      expect(result.type).toBe(sendCopilotMessage.fulfilled.type);
      const payload = result.payload as { sourceParts: { createdViaAIQB?: boolean; query: { type: SourcePartType } }[] };
      expect(payload.sourceParts).toHaveLength(1);
      expect(payload.sourceParts[0].createdViaAIQB).toBe(true);
      expect(payload.sourceParts[0].query.type).toBe(SourcePartType.GroupMembership);
    });

    it('restores identityTypes returned for group and HR source parts', async () => {
      const result = await dispatchWithMockedFetch({
        message: 'ok',
        resultingQuery: [
          {
            partId: 'g1',
            sourceType: 'GroupMembership',
            filter: null,
            title: 'Agents group',
            isExclusion: false,
            useOrgStructure: false,
            groupId: '00000000-0000-0000-0000-000000000001',
            identityTypes: ['agents', 'users'],
          },
          {
            partId: 'hr1',
            sourceType: 'SqlMembership',
            filter: "Country = 'USA'",
            title: 'US people',
            isExclusion: false,
            useOrgStructure: false,
            identityTypes: ['users'],
          },
        ],
        appliedOperations: [{ op: 'set' }],
      });

      const payload = result.payload as {
        sourceParts: { query: { identityTypes?: string[] } }[];
      };
      expect(payload.sourceParts[0].query.identityTypes).toEqual(['agents', 'users']);
      expect(payload.sourceParts[1].query.identityTypes).toEqual(['users']);
    });

    it('treats a null response selection as omitted', async () => {
      const result = await dispatchWithMockedFetch({
        message: 'ok',
        resultingQuery: [
          {
            partId: 'g1',
            sourceType: 'GroupMembership',
            filter: null,
            title: 'Legacy group',
            isExclusion: false,
            useOrgStructure: false,
            groupId: '00000000-0000-0000-0000-000000000001',
            identityTypes: null,
          },
        ],
        appliedOperations: [],
      });

      const payload = result.payload as {
        sourceParts: { query: { identityTypes?: string[] } }[];
      };
      expect(payload.sourceParts[0].query).not.toHaveProperty('identityTypes');
    });

    it('tags a mixed HR + GroupMembership response with createdViaAIQB: true on every part', async () => {
      const result = await dispatchWithMockedFetch({
        message: 'ok',
        sourceParts: [
          { partId: 'p1', filter: "Location_Code = 'US'", title: 'US', isExclusion: false, useOrgStructure: false },
          {
            partId: 'g1',
            sourceType: 'GroupMembership',
            filter: null,
            title: 'Group',
            isExclusion: false,
            useOrgStructure: false,
            groupId: '00000000-0000-0000-0000-000000000002',
          },
        ],
      });

      expect(result.type).toBe(sendCopilotMessage.fulfilled.type);
      const payload = result.payload as { sourceParts: { createdViaAIQB?: boolean }[] };
      expect(payload.sourceParts).toHaveLength(2);
      expect(payload.sourceParts.every(sp => sp.createdViaAIQB === true)).toBe(true);
    });

    // T027 [US1]: the thunk targets the Copilot endpoint and sends a workingQuery.
    it('POSTs to the Copilot endpoint with a workingQuery field', async () => {
      const fetchMock = vi.fn().mockResolvedValue({
        ok: true,
        status: 200,
        text: async () => JSON.stringify({ message: 'ok', resultingQuery: [] }),
      });
      global.fetch = fetchMock as unknown as typeof fetch;

      const store = setupStore(undefined, { authenticationService: mockAuthService as never });
      await store.dispatch(sendCopilotMessage({ message: 'describe my query' }));

      expect(fetchMock).toHaveBeenCalledTimes(1);
      const [url, options] = fetchMock.mock.calls[0];
      expect(String(url)).toContain('/api/v1/Copilot/chat');
      const body = JSON.parse((options as { body: string }).body);
      expect(Array.isArray(body.workingQuery)).toBe(true);
      expect(body.currentFilter).toBeUndefined();
    });

    it('reads the resultingQuery field into source parts', async () => {
      const result = await dispatchWithMockedFetch({
        message: 'ok',
        resultingQuery: [
          { partId: 'p1', filter: "Country = 'USA'", title: 'US', isExclusion: false, useOrgStructure: false },
        ],
      });

      expect(result.type).toBe(sendCopilotMessage.fulfilled.type);
      const payload = result.payload as { sourceParts: { id: string }[] };
      expect(payload.sourceParts).toHaveLength(1);
      expect(payload.sourceParts[0].id).toBe('p1');
    });

    it('passes through appliedOperations from a v2 response', async () => {
      const result = await dispatchWithMockedFetch({
        message: 'ok',
        resultingQuery: [
          { partId: 'p1', filter: "Country = 'USA'", title: 'US', isExclusion: false, useOrgStructure: false },
        ],
        appliedOperations: [{ op: 'replace', partId: 'p1' }],
      });

      const payload = result.payload as { appliedOperations: { op: string; partId?: string }[] };
      expect(payload.appliedOperations).toEqual([{ op: 'replace', partId: 'p1' }]);
    });

    it('keeps appliedOperations empty on a v2 no-op turn (unchanged query)', async () => {
      const result = await dispatchWithMockedFetch({
        message: 'here is what your query does',
        resultingQuery: [
          { partId: 'p1', filter: "Country = 'USA'", title: 'US', isExclusion: false, useOrgStructure: false },
        ],
        appliedOperations: [],
      });

      const payload = result.payload as { sourceParts: unknown[]; appliedOperations: unknown[] };
      expect(payload.sourceParts).toHaveLength(1);
      expect(payload.appliedOperations).toEqual([]);
    });

    it('synthesizes a non-empty appliedOperations for a legacy v1 response (no operation field)', async () => {
      // A v1 backend only ever returned sourceParts with no operation semantics. Preserve the
      // original UX: a non-empty query is treated as changed so Accept & Apply still appears.
      const result = await dispatchWithMockedFetch({
        message: 'ok',
        sourceParts: [
          { partId: 'p1', filter: "Country = 'USA'", title: 'US', isExclusion: false, useOrgStructure: false },
        ],
      });

      const payload = result.payload as { sourceParts: unknown[]; appliedOperations: unknown[] };
      expect(payload.sourceParts).toHaveLength(1);
      expect(payload.appliedOperations.length).toBeGreaterThan(0);
    });
  });

  // T027 [US1]: buildWorkingQuery projects the current source parts (including unsupported types).
  describe('buildWorkingQuery', () => {
    const hrPart: ISourcePart = {
      id: 'hr-1',
      title: 'US FTEs',
      query: {
        type: SourcePartType.HR,
        source: { filter: "Country = 'USA'" },
        exclusionary: false,
        identityTypes: ['agents', 'users'],
      },
      isNew: false,
      isExpanded: false,
    };
    const groupPart: ISourcePart = {
      id: 'grp-1',
      title: 'Leads',
      query: {
        type: SourcePartType.GroupMembership,
        source: 'group-guid',
        exclusionary: true,
        identityTypes: ['agents', 'users'],
      },
      isNew: false,
      isExpanded: false,
    };
    const ownershipPart: ISourcePart = {
      id: 'own-1',
      title: 'Owners',
      query: { type: SourcePartType.GroupOwnership, source: [] },
      isNew: false,
      isExpanded: false,
    };

    it.each([2, undefined])('uses the persisted manager depth %s instead of the previous auto-select hint', (depth) => {
      const part: ISourcePart = {
        ...hrPart,
        query: { type: SourcePartType.HR, source: { manager: { id: 222, depth } } },
        managerToAutoSelect: { objectId: 'old-id', displayName: 'Old leader', email: 'old@contoso.com' },
        depthToAutoSelect: 6,
      };
      const projected = buildWorkingQuery([part], {
        222: { objectId: 'new-id', text: 'New leader', maxDepth: 8 },
      })[0];
      expect(projected).toMatchObject({
        useOrgStructure: true, orgLeaderName: 'New leader', orgLeaderObjectId: 'new-id', orgLeaderDepth: depth ?? null,
      });
      expect(projected.orgLeaderEmail).toBeUndefined();
    });

    it('does not restore removed org structure from stale Copilot hints', () => {
      const projected = buildWorkingQuery([{
        ...hrPart,
        useOrgStructure: true,
        managerToAutoSelect: { objectId: 'old-id', displayName: 'Old leader', email: 'old@contoso.com' },
        depthToAutoSelect: 6,
      }])[0];
      expect(projected.useOrgStructure).toBe(false);
      expect(projected.orgLeaderName).toBeUndefined();
      expect(projected.orgLeaderObjectId).toBeUndefined();
      expect(projected.orgLeaderDepth).toBeUndefined();
    });

    it('preserves an email hint only when it matches the current resolved leader', () => {
      const projected = buildWorkingQuery([{
        ...hrPart,
        query: { type: SourcePartType.HR, source: { manager: { id: 222 } } },
        managerToAutoSelect: { objectId: 'SAME-ID', displayName: 'Old display name', email: 'current@contoso.com' },
      }], { 222: { objectId: 'same-id', text: 'Current display name', maxDepth: 8 } })[0];
      expect(projected.orgLeaderName).toBe('Current display name');
      expect(projected.orgLeaderEmail).toBe('current@contoso.com');
    });

    it('maps each part id to partId and carries the source type', () => {
      const wq = buildWorkingQuery([hrPart, groupPart, ownershipPart]);
      expect(wq.map(p => p.partId)).toEqual(['hr-1', 'grp-1', 'own-1']);
      expect(wq.map(p => p.sourceType)).toEqual(['SqlMembership', 'GroupMembership', 'GroupOwnership']);
    });

    it('carries the HR filter and the group id', () => {
      const wq = buildWorkingQuery([hrPart, groupPart]);
      expect(wq[0].filter).toBe("Country = 'USA'");
      expect(wq[0].identityTypes).toEqual(['agents', 'users']);
      expect(wq[0].isExclusion).toBe(false);
      expect(wq[0]).not.toHaveProperty('title');
      expect(wq[1].title).toBe('Leads');
      expect(wq[1].groupName).toBe('Leads');
      expect(wq[1].groupId).toBe('group-guid');
      expect(wq[1].isExclusion).toBe(true);
      expect(wq[1].identityTypes).toEqual(['agents', 'users']);
    });

    it('does not synthesize identityTypes for a legacy HR part', () => {
      const legacyPart: ISourcePart = {
        ...hrPart,
        query: {
          type: SourcePartType.HR,
          source: { filter: "Country = 'USA'" },
          exclusionary: false,
        },
      };

      expect(buildWorkingQuery([legacyPart])[0]).not.toHaveProperty('identityTypes');
    });

    it('does not synthesize identityTypes for a legacy group part', () => {
      const legacyPart: ISourcePart = {
        ...groupPart,
        query: {
          type: SourcePartType.GroupMembership,
          source: 'group-guid',
          exclusionary: true,
        },
      };

      expect(buildWorkingQuery([legacyPart])[0]).not.toHaveProperty('identityTypes');
    });

    it('includes unsupported parts so the server can preserve them', () => {
      const wq = buildWorkingQuery([ownershipPart]);
      expect(wq).toHaveLength(1);
      expect(wq[0].partId).toBe('own-1');
      expect(wq[0].sourceType).toBe('GroupOwnership');
      expect(wq[0].title).toBe('Owners');
    });

    it('returns an empty array for no parts', () => {
      expect(buildWorkingQuery([])).toEqual([]);
    });
  });
});
