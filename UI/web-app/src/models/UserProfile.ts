// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

/**
 * Extended user profile information from Microsoft Graph /me endpoint
 * Used to provide context to Copilot about the logged-in user
 */
export interface UserProfile {
  id: string;
  displayName: string;
  department?: string;
  companyName?: string;
  jobTitle?: string;
  manager?: {
    id: string;
    displayName: string;
    mail?: string;
    mailNickname?: string;
  };
}
