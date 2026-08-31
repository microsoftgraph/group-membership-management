// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import { clampOrgLeaderDepth, applyResolvedOrgLeaderToPart } from './orgLeader';
import { ISourcePart } from '../models/ISourcePart';
import { SourcePartType } from '../models/SourcePartType';
import { HRSourcePartSource } from '../models/HRSourcePart';

describe('clampOrgLeaderDepth', () => {
  it('keeps an existing depth that is within the leader maxDepth', () => {
    expect(clampOrgLeaderDepth(2, 5, 4)).toBe(2);
  });

  it('drops an existing depth that exceeds the leader maxDepth', () => {
    expect(clampOrgLeaderDepth(6, 3, 4)).toBeUndefined();
  });

  it('falls back to the Copilot auto-select depth when no existing depth', () => {
    expect(clampOrgLeaderDepth(undefined, 3, 10)).toBe(3);
  });

  it('is undefined when neither existing nor auto-select depth is provided', () => {
    expect(clampOrgLeaderDepth(undefined, undefined, 10)).toBeUndefined();
  });
});

describe('applyResolvedOrgLeaderToPart', () => {
  const hrPart: ISourcePart = {
    id: 'p1',
    title: "Jane's Org",
    query: { type: SourcePartType.HR, source: { filter: undefined }, exclusionary: false },
    isNew: true,
    isExpanded: false,
    useOrgStructure: true,
    managerToAutoSelect: { objectId: 'obj-1', displayName: 'Jane Smith' },
    depthToAutoSelect: 3,
  };

  it('bakes the resolved employeeId and auto-select depth into the HR source', () => {
    const result = applyResolvedOrgLeaderToPart(hrPart, 12345, 10);
    const src = result.query.source as HRSourcePartSource;
    expect(src.manager?.id).toBe(12345);
    expect(src.manager?.depth).toBe(3);
    // Non-manager fields preserved.
    expect(result.query.type).toBe(SourcePartType.HR);
    expect(result.query.exclusionary).toBe(false);
    expect(result.title).toBe("Jane's Org");
  });

  it('clamps an over-max existing depth to undefined', () => {
    const withDepth: ISourcePart = {
      ...hrPart,
      query: { type: SourcePartType.HR, source: { filter: undefined, manager: { id: undefined, depth: 8 } }, exclusionary: false },
    };
    const src = applyResolvedOrgLeaderToPart(withDepth, 999, 4).query.source as HRSourcePartSource;
    expect(src.manager?.depth).toBeUndefined();
    expect(src.manager?.id).toBe(999);
  });

  it('returns non-HR parts unchanged', () => {
    const groupPart: ISourcePart = {
      id: 'g1',
      title: 'Group',
      query: { type: SourcePartType.GroupMembership, source: 'group-guid', exclusionary: false },
      isNew: false,
      isExpanded: false,
    };
    expect(applyResolvedOrgLeaderToPart(groupPart, 1, 1)).toBe(groupPart);
  });
});
