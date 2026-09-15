// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface SubmitterActivityRow {
  submitterKey: string;
  displayName: string;
  onboarding: number;
  update: number;
  isOther: boolean;
}

export interface SubmitterDayCount {
  submitterKey: string;
  onboarding: number;
  update: number;
}

export interface OnBehalfOfActivityDay {
  date: string;
  submitters: SubmitterDayCount[];
}

export interface OnBehalfOfActivity {
  windowDays: number;
  sinceUtc: string;
  queriedAtUtc: string;
  onboardingTotal: number;
  updateTotal: number;
  grandTotal: number;
  submitters: SubmitterActivityRow[];
  days: OnBehalfOfActivityDay[];
}

export interface GetOnBehalfOfActivityResponse {
  statusCode: number;
  errorCode: string | null;
  activity: OnBehalfOfActivity;
}
