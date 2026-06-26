// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { hasTrailingAndOrOperator, hasValueAfterOperator, removeTrailingAndOrOperator } from './filterValidationHelpers';

describe('hasTrailingAndOrOperator', () => {
  it('returns false for null/undefined/empty string', () => {
    expect(hasTrailingAndOrOperator('')).toBe(false);
    expect(hasTrailingAndOrOperator('   ')).toBe(false);
    expect(hasTrailingAndOrOperator(null as unknown as string)).toBe(false);
    expect(hasTrailingAndOrOperator(undefined as unknown as string)).toBe(false);
  });

  it('returns true when filter ends with AND', () => {
    expect(hasTrailingAndOrOperator('department eq "Sales" and')).toBe(true);
    expect(hasTrailingAndOrOperator('department eq "Sales" AND')).toBe(true);
    expect(hasTrailingAndOrOperator('department eq "Sales" And ')).toBe(true);
  });

  it('returns true when filter ends with OR', () => {
    expect(hasTrailingAndOrOperator('department eq "Sales" or')).toBe(true);
    expect(hasTrailingAndOrOperator('department eq "Sales" OR')).toBe(true);
    expect(hasTrailingAndOrOperator('department eq "Sales" Or ')).toBe(true);
  });

  it('returns false when filter does not end with AND/OR', () => {
    expect(hasTrailingAndOrOperator('department eq "Sales"')).toBe(false);
    expect(hasTrailingAndOrOperator('name eq "Anderson"')).toBe(false);
    expect(hasTrailingAndOrOperator('city eq "Orlando"')).toBe(false);
  });
});

describe('removeTrailingAndOrOperator', () => {
  it('returns input unchanged for null/undefined/empty string', () => {
    expect(removeTrailingAndOrOperator('')).toBe('');
    expect(removeTrailingAndOrOperator('   ')).toBe('   ');
    expect(removeTrailingAndOrOperator(null as unknown as string)).toBe(null);
    expect(removeTrailingAndOrOperator(undefined as unknown as string)).toBe(undefined);
  });

  it('removes trailing AND operator', () => {
    expect(removeTrailingAndOrOperator('department eq "Sales" and')).toBe('department eq "Sales"');
    expect(removeTrailingAndOrOperator('department eq "Sales" AND ')).toBe('department eq "Sales"');
  });

  it('removes trailing OR operator', () => {
    expect(removeTrailingAndOrOperator('department eq "Sales" or')).toBe('department eq "Sales"');
    expect(removeTrailingAndOrOperator('department eq "Sales" OR ')).toBe('department eq "Sales"');
  });

  it('does not modify filter without trailing operator', () => {
    expect(removeTrailingAndOrOperator('department eq "Sales"')).toBe('department eq "Sales"');
    expect(removeTrailingAndOrOperator('name eq "Anderson"')).toBe('name eq "Anderson"');
  });
});

describe('hasValueAfterOperator', () => {
  it('returns false for empty/whitespace input', () => {
    expect(hasValueAfterOperator('')).toBe(false);
    expect(hasValueAfterOperator('   ')).toBe(false);
  });

  it('returns false when operator has no value (e.g. "Col IN")', () => {
    expect(hasValueAfterOperator('CompanyType IN')).toBe(false);
    expect(hasValueAfterOperator('CompanyType NOT IN')).toBe(false);
    expect(hasValueAfterOperator('Height >')).toBe(false);
  });

  it('returns false for dangling empty parens after IN/NOT IN', () => {
    expect(hasValueAfterOperator("CompanyType IN ()")).toBe(false);
    expect(hasValueAfterOperator("CompanyType NOT IN ( )")).toBe(false);
  });

  it('returns true for complete single-row filters', () => {
    expect(hasValueAfterOperator("CompanyType IN ('MS','LI')")).toBe(true);
    expect(hasValueAfterOperator("Height > 2")).toBe(true);
    expect(hasValueAfterOperator("Department = 'Sales'")).toBe(true);
  });

  it('returns true when every segment has a value', () => {
    expect(hasValueAfterOperator("CompanyType IN ('MS') AND Height > 2")).toBe(true);
    expect(hasValueAfterOperator("Dept = 'Sales' OR Dept = 'Eng'")).toBe(true);
  });

  it('returns false when any segment is missing a value', () => {
    expect(hasValueAfterOperator("CompanyType IN ('MS') AND Height >")).toBe(false);
  });

  it('handles grouped filters with outer parentheses', () => {
    expect(hasValueAfterOperator("(CompanyType IN ('MS') AND Height > 2) OR Building = '102682'")).toBe(true);
  });
});
