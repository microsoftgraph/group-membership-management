// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, test } from 'vitest';
import { getDefaultAlertBannerConfig, normalizeAlertBannerWindow, AlertBannerConfig } from './AlertBannerConfig';

const baseConfig = (overrides?: Partial<AlertBannerConfig>): AlertBannerConfig => ({
  message: '',
  isEnabled: false,
  startDate: '2026-06-01T00:00:00.000Z',
  endDate: '2026-06-10T00:00:00.000Z',
  linkUrl: null,
  linkText: null,
  ...overrides,
});

describe('getDefaultAlertBannerConfig', () => {
  test('produces a window that satisfies the start-before-end rule', () => {
    const config = getDefaultAlertBannerConfig();

    expect(new Date(config.startDate).getTime()).toBeLessThan(new Date(config.endDate).getTime());
    expect(config.isEnabled).toBe(false);
  });
});

describe('normalizeAlertBannerWindow', () => {
  test('widens a degenerate window returned by older backends', () => {
    const config = baseConfig({ endDate: '2026-06-01T00:00:00.000Z' });

    const normalized = normalizeAlertBannerWindow(config);

    expect(normalized.startDate).toBe(config.startDate);
    expect(new Date(normalized.endDate).getTime()).toBeGreaterThan(new Date(config.startDate).getTime());
  });

  test('widens a window whose end precedes its start', () => {
    const config = baseConfig({ startDate: '2026-06-10T00:00:00.000Z', endDate: '2026-06-01T00:00:00.000Z' });

    const normalized = normalizeAlertBannerWindow(config);

    expect(new Date(normalized.endDate).getTime()).toBeGreaterThan(new Date(normalized.startDate).getTime());
  });

  test('returns a valid configuration unchanged so the form is not marked dirty', () => {
    const config = baseConfig();

    expect(normalizeAlertBannerWindow(config)).toBe(config);
  });

  test('leaves unparseable dates alone so the validation error still surfaces', () => {
    const config = baseConfig({ endDate: 'not-a-date' });

    expect(normalizeAlertBannerWindow(config)).toBe(config);
  });
});
