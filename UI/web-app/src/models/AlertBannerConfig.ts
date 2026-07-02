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
 * or when no configuration exists.
 */
export const getDefaultAlertBannerConfig = (): AlertBannerConfig => {
  const now = new Date().toISOString();
  return {
    message: '',
    isEnabled: false,
    startDate: now,
    endDate: now,
    linkUrl: null,
    linkText: null,
  };
};
