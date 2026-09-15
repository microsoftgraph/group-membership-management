// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ApprovalActivity, DashboardSummary, OnBehalfOfActivity } from '../../models';

export interface IDashboardApi {
  fetchDashboardSummary(): Promise<DashboardSummary>;
  fetchApprovalActivity(windowDays: number): Promise<ApprovalActivity>;
  fetchOnBehalfOfActivity(windowDays: number): Promise<OnBehalfOfActivity>;
}
