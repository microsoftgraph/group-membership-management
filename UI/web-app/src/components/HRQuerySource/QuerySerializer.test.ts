// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import { computeInClauseSelection, getSelectedKeys, joinFilterSegments, parseGroup } from './QuerySerializer';

// Helper mirroring how the component turns the selected keys into an IN clause value.
const toInClause = (keys: string[]): string => `(${keys.map(k => `'${k}'`).join(', ')})`;

describe('getSelectedKeys', () => {
  it('returns an empty array for an empty string', () => {
    expect(getSelectedKeys('')).toEqual([]);
  });

  it('parses a single quoted value', () => {
    expect(getSelectedKeys("('FTE')")).toEqual(['FTE']);
  });

  it('parses a multi-value IN clause with parentheses', () => {
    expect(getSelectedKeys("('FTE', 'Intern')")).toEqual(['FTE', 'Intern']);
  });

  it('parses a multi-value IN clause with brackets', () => {
    expect(getSelectedKeys('[FTE, Intern]')).toEqual(['FTE', 'Intern']);
  });

  it('preserves an embedded apostrophe by decoding SQL-doubled quotes', () => {
    expect(getSelectedKeys("('O''Brien')")).toEqual(["O'Brien"]);
  });

  it('preserves a comma inside a quoted value instead of splitting it', () => {
    expect(getSelectedKeys("('A,B')")).toEqual(['A,B']);
  });

  it('parses a mix of a quoted apostrophe value and a plain value', () => {
    expect(getSelectedKeys("('O''Brien', 'FTE')")).toEqual(["O'Brien", 'FTE']);
  });
});

describe('computeInClauseSelection', () => {
  it('adds the first selected key when the row has no existing value', () => {
    expect(computeInClauseSelection('', 'FTE', true)).toEqual(['FTE']);
    expect(computeInClauseSelection(undefined, 'FTE', true)).toEqual(['FTE']);
  });

  it('appends an additional selected key to the same row', () => {
    expect(computeInClauseSelection("('FTE')", 'Intern', true)).toEqual(['FTE', 'Intern']);
  });

  it('removes a deselected key', () => {
    expect(computeInClauseSelection("('FTE', 'Intern')", 'Intern', false)).toEqual(['FTE']);
  });

  it('does not duplicate a key that is re-selected', () => {
    expect(computeInClauseSelection("('FTE', 'Intern')", 'FTE', true)).toEqual(['Intern', 'FTE']);
  });

  // Regression: selecting values on one attribute row must never leak into another row's
  // IN clause. Each row's selections are derived only from that row's own current value.
  it('derives selections only from the current row (no cross-row leak)', () => {
    // Row 1 (EmployeeType) already has two selected values.
    const employeeTypeKeys = computeInClauseSelection("('FTE', 'Intern')", 'Intern', true);
    expect(toInClause(employeeTypeKeys)).toBe("('FTE', 'Intern')");

    // Row 2 (Profession) is brand new and only the user's single pick should appear,
    // NOT the values selected on the EmployeeType row.
    const professionKeys = computeInClauseSelection('', '11000057', true);
    expect(professionKeys).toEqual(['11000057']);
    expect(toInClause(professionKeys)).toBe("('11000057')");
  });

  it('builds a clean two-value IN clause across sequential picks', () => {
    // First pick on an empty row.
    const afterFirst = computeInClauseSelection('', 'FTE', true);
    expect(toInClause(afterFirst)).toBe("('FTE')");

    // Second pick reads the row's now-current value and folds into the same IN clause,
    // rather than spilling into a malformed "IN ('FTE') Or ... = 'Intern'" fragment.
    const afterSecond = computeInClauseSelection(toInClause(afterFirst), 'Intern', true);
    expect(toInClause(afterSecond)).toBe("('FTE', 'Intern')");
  });

  // Helper mirroring the component's escaped serialization (SQL-doubled apostrophes).
  const toEscapedInClause = (keys: string[]): string =>
    `(${keys.map(k => `'${k.replace(/'/g, "''")}'`).join(', ')})`;

  it('round-trips a value containing an apostrophe without losing it', () => {
    const afterFirst = computeInClauseSelection('', "O'Brien", true);
    expect(afterFirst).toEqual(["O'Brien"]);
    const serialized = toEscapedInClause(afterFirst);
    expect(serialized).toBe("('O''Brien')");

    // Re-reading the serialized row must yield the original key so a subsequent
    // toggle does not corrupt or drop the O'Brien selection.
    const afterSecond = computeInClauseSelection(serialized, 'FTE', true);
    expect(afterSecond).toEqual(["O'Brien", 'FTE']);
  });
});

describe('joinFilterSegments', () => {
  it('preserves the separator after changing a middle filter to IN', () => {
    const segments = [
      "LocationArea_Code IN ('AL', 'CA') And ",
      "EmployeeType_Code IN ('FTE') And",
      "CitySummary_Code IN ('ADELAIDE', 'AKRON')",
    ];

    expect(joinFilterSegments(segments)).toBe(
      "LocationArea_Code IN ('AL', 'CA') And EmployeeType_Code IN ('FTE') And CitySummary_Code IN ('ADELAIDE', 'AKRON')"
    );
  });

  it('preserves whitespace inside quoted values', () => {
    expect(joinFilterSegments(["DisplayName = 'Ada  Lovelace' And ", "EmployeeType = 'FTE'"]))
      .toBe("DisplayName = 'Ada  Lovelace' And EmployeeType = 'FTE'");
  });
});

describe('parseGroup', () => {
  const validGroupedFilter =
    "(JobFunction_Code = 'O4A' Or JobFunction_Code = 'O2H') And  (Vertical_Code = '104' Or Vertical_Code = '280') And  (Height = 2)  ";

  it('parses a well-formed grouped filter into its groups', () => {
    const groups = parseGroup(validGroupedFilter, false);
    expect(groups).toHaveLength(3);
  });

  // Regression: an invalid grouped sub-filter with an empty value ("JobFunction_Code = ") must return [] (not throw) so the caller shows the raw query as text (query preserved) instead of white-screening the Job Details page.
  it('returns [] (does not throw) when an earlier grouped sub-filter is invalid', () => {
    const malformedFilter =
      "(JobFunction_Code = 'O2H' Or JobFunction_Code = 'N1J' Or JobFunction_Code = ) And  (Vertical_Code = '104' Or Vertical_Code = '280') And  (Height = 2)  ";
    let groups: unknown;
    expect(() => { groups = parseGroup(malformedFilter, false); }).not.toThrow();
    expect(groups).toEqual([]);
  });

  it('returns [] (does not throw) when the empty-value sub-group is not the first group', () => {
    const malformedFilter =
      "(Height = 2) And  (JobFunction_Code = 'O2H' Or JobFunction_Code = ) And  (Vertical_Code = '104')  ";
    let groups: unknown;
    expect(() => { groups = parseGroup(malformedFilter, false); }).not.toThrow();
    expect(groups).toEqual([]);
  });
});
