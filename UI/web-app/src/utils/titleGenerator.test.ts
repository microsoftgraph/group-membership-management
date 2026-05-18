// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import {
  generateHRTitle,
  extractOrgLeaderName,
  extractDepthFromTitle,
  extractExclusionaryFromTitle,
  removeExclusionaryPrefix,
  extractCriteriaFromTitle,
  updateHRTitleWithNewLeader,
  updateHRTitleWithNewDepth,
  combineHRTitleWithAICriteria,
  generateGroupTitle,
} from './titleGenerator';

describe('generateHRTitle', () => {
  it('returns org leader title when no depth', () => {
    expect(generateHRTitle({ orgLeaderName: 'Alice' })).toBe("Everyone in Alice's org");
  });

  it('returns org leader title when depth is 0', () => {
    expect(generateHRTitle({ orgLeaderName: 'Bob', depth: 0 })).toBe("Everyone in Bob's org");
  });

  it('returns org leader title when depth is 1 (levels = 0)', () => {
    expect(generateHRTitle({ orgLeaderName: 'Carol', depth: 1 })).toBe("Everyone in Carol's org");
  });

  it('returns single level title when depth is 2 (levels = 1)', () => {
    expect(generateHRTitle({ orgLeaderName: 'Dan', depth: 2 })).toBe('1 level of direct reports of Dan');
  });

  it('returns multiple levels title when depth is 4 (levels = 3)', () => {
    expect(generateHRTitle({ orgLeaderName: 'Eve', depth: 4 })).toBe('3 levels of direct reports of Eve');
  });

  it('adds Exclude prefix when exclusionary', () => {
    expect(generateHRTitle({ orgLeaderName: 'Frank', exclusionary: true })).toBe("Exclude Everyone in Frank's org");
  });

  it('uses custom templates', () => {
    const templates = {
      excludePrefix: 'Excl',
      orgLeaderTitle: 'Todos en la org de {0}',
    };
    expect(generateHRTitle({ orgLeaderName: 'Grace', exclusionary: true }, templates)).toBe('Excl Todos en la org de Grace');
  });
});

describe('extractOrgLeaderName', () => {
  it('extracts from org pattern', () => {
    expect(extractOrgLeaderName("Everyone in Alice's org")).toBe('Alice');
  });

  it('extracts from direct reports pattern', () => {
    expect(extractOrgLeaderName('3 levels of direct reports of Bob')).toBe('Bob');
  });

  it('extracts from single level pattern', () => {
    expect(extractOrgLeaderName('1 level of direct reports of Carol')).toBe('Carol');
  });

  it('extracts with trailing "with" criteria', () => {
    expect(extractOrgLeaderName('2 levels of direct reports of Dan with criteria')).toBe('Dan');
  });

  it('returns empty for unrecognized title', () => {
    expect(extractOrgLeaderName('Some random title')).toBe('');
  });
});

describe('extractDepthFromTitle', () => {
  it('extracts depth from levels pattern', () => {
    expect(extractDepthFromTitle('3 levels of direct reports of Alice')).toBe(4);
  });

  it('extracts depth from single level pattern', () => {
    expect(extractDepthFromTitle('1 level of direct reports of Bob')).toBe(2);
  });

  it('returns undefined for org pattern', () => {
    expect(extractDepthFromTitle("Everyone in Carol's org")).toBeUndefined();
  });
});

describe('extractExclusionaryFromTitle', () => {
  it('returns true for excluded title', () => {
    expect(extractExclusionaryFromTitle("Exclude Everyone in Alice's org")).toBe(true);
  });

  it('returns false for normal title', () => {
    expect(extractExclusionaryFromTitle("Everyone in Alice's org")).toBe(false);
  });

  it('uses custom prefix', () => {
    expect(extractExclusionaryFromTitle("Excl Everyone", "Excl")).toBe(true);
  });
});

describe('removeExclusionaryPrefix', () => {
  it('removes prefix', () => {
    expect(removeExclusionaryPrefix("Exclude Everyone in Alice's org")).toBe("Everyone in Alice's org");
  });

  it('returns unchanged if no prefix', () => {
    expect(removeExclusionaryPrefix("Everyone in Alice's org")).toBe("Everyone in Alice's org");
  });
});

describe('extractCriteriaFromTitle', () => {
  it('extracts criteria after separator', () => {
    expect(extractCriteriaFromTitle("Everyone in Alice's org with the following summarized criteria: Dept eq Sales")).toBe('Dept eq Sales');
  });

  it('returns empty when no criteria', () => {
    expect(extractCriteriaFromTitle("Everyone in Alice's org")).toBe('');
  });
});

describe('updateHRTitleWithNewLeader', () => {
  it('generates new title from empty', () => {
    expect(updateHRTitleWithNewLeader('', 'Alice')).toBe("Everyone in Alice's org");
  });

  it('preserves depth when changing leader', () => {
    expect(updateHRTitleWithNewLeader('3 levels of direct reports of Bob', 'Alice')).toBe('3 levels of direct reports of Alice');
  });

  it('preserves exclusionary and criteria', () => {
    const title = "Exclude 1 level of direct reports of Bob with the following summarized criteria: dept eq Sales";
    const result = updateHRTitleWithNewLeader(title, 'Alice');
    expect(result).toContain('Alice');
    expect(result).toContain('Exclude');
    expect(result).toContain('dept eq Sales');
  });
});

describe('updateHRTitleWithNewDepth', () => {
  it('returns empty title unchanged', () => {
    expect(updateHRTitleWithNewDepth('', 3)).toBe('');
  });

  it('returns title unchanged when leader cannot be extracted', () => {
    expect(updateHRTitleWithNewDepth('Random title', 3)).toBe('Random title');
  });

  it('updates depth for existing title', () => {
    const result = updateHRTitleWithNewDepth("Everyone in Alice's org", 4);
    expect(result).toBe('3 levels of direct reports of Alice');
  });

  it('preserves criteria when updating depth', () => {
    const title = "Everyone in Alice's org with the following summarized criteria: x";
    const result = updateHRTitleWithNewDepth(title, 3);
    expect(result).toContain('Alice');
    expect(result).toContain('x');
  });
});

describe('combineHRTitleWithAICriteria', () => {
  it('returns existing title when no AI title', () => {
    expect(combineHRTitleWithAICriteria('existing', undefined, false)).toBe('existing');
  });

  it('returns AI title when no existing title', () => {
    expect(combineHRTitleWithAICriteria('', 'AI generated', false)).toBe('AI generated');
  });

  it('returns AI title with exclude when exclusionary and no existing', () => {
    expect(combineHRTitleWithAICriteria('', 'AI generated', false, undefined, true)).toBe('Exclude AI generated');
  });

  it('combines HR manager title with AI criteria', () => {
    const result = combineHRTitleWithAICriteria("Everyone in Alice's org", 'dept eq Sales', true);
    expect(result).toContain("Everyone in Alice's org");
    expect(result).toContain('dept eq Sales');
  });

  it('does not add criteria twice', () => {
    const existing = "Everyone in Alice's org with the following summarized criteria: old";
    expect(combineHRTitleWithAICriteria(existing, 'new', true)).toBe(existing);
  });

  it('returns existing when not HR with manager', () => {
    expect(combineHRTitleWithAICriteria('existing title', 'AI', false)).toBe('existing title');
  });
});

describe('generateGroupTitle', () => {
  it('returns group name in template', () => {
    expect(generateGroupTitle('My Group')).toBe('All Users in My Group');
  });

  it('uses fallback source when no group name', () => {
    expect(generateGroupTitle(undefined, 'source-id')).toBe('All Users in source-id');
  });

  it('uses generic fallback when neither', () => {
    expect(generateGroupTitle(undefined)).toBe('All Users in Group');
  });

  it('adds exclude prefix when exclusionary', () => {
    expect(generateGroupTitle('Group', undefined, true)).toBe('Exclude All Users in Group');
  });

  it('uses empty group name with whitespace as fallback', () => {
    expect(generateGroupTitle('  ', 'src')).toBe('All Users in src');
  });

  it('uses generic fallback for empty both', () => {
    expect(generateGroupTitle('', '')).toBe('All Users in Group');
  });

  it('uses custom templates', () => {
    expect(generateGroupTitle('G', undefined, false, { allUsersInGroup: 'Todos en {0}' })).toBe('Todos en G');
  });
});
