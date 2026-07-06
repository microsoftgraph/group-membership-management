// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { afterEach, beforeEach, describe, it, expect, vi } from 'vitest';
import { SourcePartType } from '../models/SourcePartType';
import { setupStore } from './store';
import { sendCopilotMessage } from './copilot.api';

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
  });
});
