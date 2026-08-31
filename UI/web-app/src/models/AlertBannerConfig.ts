// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

/**
 * Client-side shape of the alert banner configuration.
 * Mirrors the backend AlertBannerConfigDto. All dates are ISO 8601 UTC strings.
 */
export interface AlertBannerConfig {
  message: string;
  isEnabled: boolean;
  startDate: string;
  endDate: string;
  linkUrl: string | null;
  linkText: string | null;
}

/**
 * Number of days the default visibility window spans. A non-empty window keeps the
 * disabled default valid against the server's start-before-end rule.
 */
const DEFAULT_WINDOW_DAYS = 7;

const addDays = (date: Date, days: number): Date => new Date(date.getTime() + days * 24 * 60 * 60 * 1000);

/**
 * Returns a disabled default configuration, used before the server config loads
 * or when no configuration exists. The window spans a week so the default passes
 * the server's start-before-end validation without any date edits.
 */
export const getDefaultAlertBannerConfig = (): AlertBannerConfig => {
  const now = new Date();
  return {
    message: '',
    isEnabled: false,
    startDate: now.toISOString(),
    endDate: addDays(now, DEFAULT_WINDOW_DAYS).toISOString(),
    linkUrl: null,
    linkText: null,
  };
};

/**
 * Widens a degenerate visibility window so the admin form does not open with a
 * validation error the admin never caused. Older backends (and the pre-existing
 * default configuration) return start == end when no banner has been configured,
 * which fails the server's start-before-end rule and would otherwise block saving
 * every other setting on the General tab. Configurations that already describe a
 * real window are returned untouched.
 */
export const normalizeAlertBannerWindow = (config: AlertBannerConfig): AlertBannerConfig => {
  const start = new Date(config.startDate);
  const end = new Date(config.endDate);

  if (isNaN(start.getTime()) || isNaN(end.getTime()) || start < end) {
    return config;
  }

  return { ...config, endDate: addDays(start, DEFAULT_WINDOW_DAYS).toISOString() };
};
