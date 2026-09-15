// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { ApprovalActivity, DashboardSummary } from '../models';
import { ThunkConfig } from './store';

export const fetchDashboardSummary = createAsyncThunk<DashboardSummary, void, ThunkConfig>(
  'dashboard/fetchDashboardSummary',
  async (_, { extra }) => {
    const { gmmApi } = extra.apis;
    return await gmmApi.dashboard.fetchDashboardSummary();
  }
);

export const fetchApprovalActivity = createAsyncThunk<ApprovalActivity, number, ThunkConfig>(
  'dashboard/fetchApprovalActivity',
  async (windowDays, { extra }) => {
    const { gmmApi } = extra.apis;
    return await gmmApi.dashboard.fetchApprovalActivity(windowDays);
  }
);
