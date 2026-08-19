// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { AxiosResponse } from 'axios';
import { ApiBase } from '../ApiBase';
import { IFeedbackApi, RefineFeedbackResponse } from './IFeedbackApi';

export class FeedbackApi extends ApiBase implements IFeedbackApi {
  public async refineFeedback(feedback: string): Promise<AxiosResponse<RefineFeedbackResponse>> {
    const response = await this.httpClient.post<RefineFeedbackResponse>('/refine', { feedback });
    this.ensureSuccessStatusCode(response);
    return response;
  }
}
