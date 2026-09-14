// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { DashboardSummary, GetDashboardSummaryResponse } from '../../models';
import { ApiBase } from '../ApiBase';
import { IDashboardApi } from './IDashboardApi';

export class DashboardApi extends ApiBase implements IDashboardApi {
  public async fetchDashboardSummary(): Promise<DashboardSummary> {
    const response = await this.httpClient.get<GetDashboardSummaryResponse>('/summary');
    this.ensureSuccessStatusCode(response);
    return response.data.summary;
  }
}
