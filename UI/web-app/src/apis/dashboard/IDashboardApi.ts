// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ApprovalActivity, DashboardSummary } from '../../models';

export interface IDashboardApi {
  fetchDashboardSummary(): Promise<DashboardSummary>;
  fetchApprovalActivity(windowDays: number): Promise<ApprovalActivity>;
}
