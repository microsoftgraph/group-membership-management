// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  DashboardSummary,
  GetDashboardSummaryResponse,
  ApprovalActivity,
  GetApprovalActivityResponse,
  OnBehalfOfActivity,
  GetOnBehalfOfActivityResponse,
} from '../../models';
import { ApiBase } from '../ApiBase';
import { IDashboardApi } from './IDashboardApi';

export class DashboardApi extends ApiBase implements IDashboardApi {
  public async fetchDashboardSummary(): Promise<DashboardSummary> {
    const response = await this.httpClient.get<GetDashboardSummaryResponse>('/summary');
    this.ensureSuccessStatusCode(response);
    return response.data.summary;
  }

  public async fetchApprovalActivity(windowDays: number): Promise<ApprovalActivity> {
    const response = await this.httpClient.get<GetApprovalActivityResponse>(`/approvals?windowDays=${windowDays}`);
    this.ensureSuccessStatusCode(response);
    return response.data.activity;
  }

  public async fetchOnBehalfOfActivity(windowDays: number): Promise<OnBehalfOfActivity> {
    const response = await this.httpClient.get<GetOnBehalfOfActivityResponse>(`/onbehalf?windowDays=${windowDays}`);
    this.ensureSuccessStatusCode(response);
    return response.data.activity;
  }
}
