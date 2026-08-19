// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { createAsyncThunk } from '@reduxjs/toolkit';
import { RefineFeedbackError } from '../apis/feedback';
import { ThunkConfigWithErrors } from './store';

export type RefineFeedbackFailureCode =
  | 'InvalidRequest'
  | 'FeedbackTooLong'
  | 'FeatureDisabled'
  | 'InvalidRefinedText'
  | 'RefinedTextTooLong'
  | 'Timeout'
  | 'ServiceUnavailable'
  | 'InternalError'
  | 'NetworkError';

export interface RefineFeedbackFailure {
  code: RefineFeedbackFailureCode;
  message: string;
}

const knownFailureCodes: RefineFeedbackFailureCode[] = [
  'InvalidRequest',
  'FeedbackTooLong',
  'FeatureDisabled',
  'InvalidRefinedText',
  'RefinedTextTooLong',
  'Timeout',
  'ServiceUnavailable',
  'InternalError',
];

const statusToFailureCode: Record<number, RefineFeedbackFailureCode> = {
  400: 'InvalidRequest',
  408: 'Timeout',
  422: 'InvalidRefinedText',
  500: 'InternalError',
  503: 'ServiceUnavailable',
};

// Reads only the status and the documented error body. The Axios error object itself is never
// logged or stored because it carries the request configuration, which includes the feedback.
const toFailure = (error: unknown): RefineFeedbackFailure => {
  const response = (error as { response?: { status?: number; data?: Partial<RefineFeedbackError> } })?.response;

  if (!response || typeof response.status !== 'number') {
    return { code: 'NetworkError', message: 'Feedback refinement could not be reached.' };
  }

  const bodyCode = response.data?.code;
  const code = knownFailureCodes.find((known) => known === bodyCode)
    ?? statusToFailureCode[response.status]
    ?? 'InternalError';

  return { code, message: response.data?.error ?? 'Feedback refinement failed.' };
};

export const refineFeedback = createAsyncThunk<
  string,
  string,
  ThunkConfigWithErrors<RefineFeedbackFailure>
>('feedback/refine', async (feedback, { extra, rejectWithValue }) => {
  if (!feedback || !feedback.trim()) {
    return rejectWithValue({ code: 'InvalidRequest', message: 'Feedback cannot be empty.' });
  }

  try {
    const response = await extra.apis.gmmApi.feedback.refineFeedback(feedback);
    const refinedText = response.data?.refinedText;

    if (!refinedText || !refinedText.trim()) {
      return rejectWithValue({ code: 'InvalidRefinedText', message: 'Refined feedback could not be produced.' });
    }

    return refinedText;
  } catch (error) {
    return rejectWithValue(toFailure(error));
  }
});
