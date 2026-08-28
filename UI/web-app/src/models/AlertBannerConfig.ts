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
 * Returns a disabled default configuration, used before the server config loads
 * or when no configuration exists. The window spans a week so the default passes
 * the server's start-before-end validation without any date edits.
 */
export const getDefaultAlertBannerConfig = (): AlertBannerConfig => {
  const now = new Date();
  const weekFromNow = new Date(now.getTime() + 7 * 24 * 60 * 60 * 1000);
  return {
    message: '',
    isEnabled: false,
    startDate: now.toISOString(),
    endDate: weekFromNow.toISOString(),
    linkUrl: null,
    linkText: null,
  };
};
