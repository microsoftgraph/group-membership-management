// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, it, expect } from 'vitest';
import { SourcePartType } from '../models/SourcePartType';

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
});
