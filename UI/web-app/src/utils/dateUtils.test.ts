// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import { formatLastRunTime, formatNextRunTime } from './dateUtils';

const SQLMinDate = new Date(Date.UTC(1753, 0, 1));

describe('formatLastRunTime', () => {
  it('returns SQL min date formatted when input matches SQL min date', () => {
    const result = formatLastRunTime(SQLMinDate.toISOString());
    expect(result[0]).toBe(SQLMinDate.toLocaleDateString());
    expect(result[1]).toBe(0);
  });

  it('returns formatted date and hours ago for a valid date', () => {
    const twoHoursAgo = new Date(Date.now() - 2 * 60 * 60 * 1000).toISOString();
    const result = formatLastRunTime(twoHoursAgo);
    expect(result[1]).toBeGreaterThanOrEqual(1);
    expect(result[1]).toBeLessThanOrEqual(3);
  });

  it('appends Z when input does not end with Z', () => {
    const dateWithoutZ = '2025-06-01T12:00:00';
    const result = formatLastRunTime(dateWithoutZ);
    expect(result[0]).toBeDefined();
    expect(typeof result[1]).toBe('number');
  });

  it('handles input already ending with Z', () => {
    const dateWithZ = '2025-06-01T12:00:00Z';
    const result = formatLastRunTime(dateWithZ);
    expect(result[0]).toBeDefined();
  });
});

describe('formatNextRunTime', () => {
  it('returns dash when disabled', () => {
    const result = formatNextRunTime('2025-06-01T12:00:00Z', false);
    expect(result[0]).toBe('-');
    expect(result[1]).toBe(0);
  });

  it('returns formatted date and hours left when enabled', () => {
    const futureDate = new Date(Date.now() + 5 * 60 * 60 * 1000).toISOString();
    const result = formatNextRunTime(futureDate, true);
    expect(result[0]).not.toBe('-');
    expect(result[1]).toBeGreaterThanOrEqual(4);
    expect(result[1]).toBeLessThanOrEqual(6);
  });

  it('appends Z when input does not end with Z', () => {
    const dateWithoutZ = '2025-06-01T12:00:00';
    const result = formatNextRunTime(dateWithoutZ, true);
    expect(result[0]).toBeDefined();
  });
});
