// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { AxiosResponse } from 'axios';

export interface RefineFeedbackResponse {
  refinedText: string;
}

export interface RefineFeedbackError {
  error: string;
  code: string;
}

export interface IFeedbackApi {
  refineFeedback(feedback: string): Promise<AxiosResponse<RefineFeedbackResponse>>;
}
