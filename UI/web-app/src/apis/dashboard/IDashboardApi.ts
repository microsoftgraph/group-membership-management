// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { DashboardSummary } from '../../models';

export interface IDashboardApi {
  fetchDashboardSummary(): Promise<DashboardSummary>;
}
