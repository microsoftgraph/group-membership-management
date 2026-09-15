// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

export interface ReviewerActivityRow {
  reviewerKey: string;
  displayName: string;
  approved: number;
  rejected: number;
  isOther: boolean;
}

export interface ReviewerDayCount {
  reviewerKey: string;
  approved: number;
  rejected: number;
}

export interface ApprovalActivityDay {
  date: string;
  automated: number;
  reviewers: ReviewerDayCount[];
}

export interface ApprovalActivity {
  windowDays: number;
  sinceUtc: string;
  queriedAtUtc: string;
  automatedTotal: number;
  manualTotal: number;
  rejectedTotal: number;
  reviewers: ReviewerActivityRow[];
  days: ApprovalActivityDay[];
}

export interface GetApprovalActivityResponse {
  statusCode: number;
  errorCode: string | null;
  activity: ApprovalActivity;
}
