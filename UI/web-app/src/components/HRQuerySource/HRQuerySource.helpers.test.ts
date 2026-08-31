// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import { shouldReparseHrFilter } from './HRQuerySource.base';

describe('shouldReparseHrFilter', () => {
  it('re-parses a normal (non-grouped) filter change', () => {
    expect(shouldReparseHrFilter("EmployeeType = 'Intern'", false, false)).toBe(true);
  });

  it('does NOT re-parse an interactive edit round-trip once grouping is latched on', () => {
    // groupingEnabled=true and not an external change: the builder already reflects the edit,
    // so re-parsing here would clobber in-progress grouped editing.
    expect(shouldReparseHrFilter("(A = '1' Or B = '2')", true, false)).toBe(false);
  });

  it('re-parses when a grouped filter is replaced externally (Copilot refine on the same part)', () => {
    // Regression guard: the grouping latch previously froze the builder on an external
    // replacement, leaving stale rows on screen until the rule was reselected.
    expect(shouldReparseHrFilter("(A = '1' Or B = '2' Or C = '3')", true, true)).toBe(true);
  });

  it('does not re-parse an empty or undefined filter', () => {
    expect(shouldReparseHrFilter(undefined, false, true)).toBe(false);
    expect(shouldReparseHrFilter('', true, true)).toBe(false);
  });
});
