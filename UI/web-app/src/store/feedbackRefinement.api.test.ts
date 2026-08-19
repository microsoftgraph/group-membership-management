// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { configureStore } from '@reduxjs/toolkit';
import { describe, expect, it, vi } from 'vitest';
import { refineFeedback, type RefineFeedbackFailure } from './feedbackRefinement.api';

const feedback = 'the exclusions are not clear. please explain why each one is needed';

const createStore = (refineFeedbackMock: ReturnType<typeof vi.fn>) =>
  configureStore({
    reducer: (state = {}) => state,
    middleware: (getDefaultMiddleware) =>
      getDefaultMiddleware({
        thunk: {
          extraArgument: {
            apis: { gmmApi: { feedback: { refineFeedback: refineFeedbackMock } } },
            services: {},
          },
        },
        serializableCheck: false,
      }),
  });

const axiosErrorWith = (status: number, code?: string, error?: string) => ({
  isAxiosError: true,
  config: { data: JSON.stringify({ feedback }) },
  response: { status, data: code ? { code, error } : undefined },
});

type ThunkResult = { type: string; payload: unknown };

// The local store uses a placeholder reducer, so its dispatch is not typed for this thunk.
const dispatchRefine = async (store: ReturnType<typeof createStore>, input: string): Promise<ThunkResult> =>
  (await store.dispatch(refineFeedback(input) as never)) as unknown as ThunkResult;

describe('refineFeedback thunk', () => {
  it('posts the feedback and fulfills with the refined text', async () => {
    const api = vi.fn().mockResolvedValue({ data: { refinedText: 'Refined.' } });
    const store = createStore(api);

    const result = await dispatchRefine(store, feedback);

    expect(api).toHaveBeenCalledWith(feedback);
    expect(result.payload).toBe('Refined.');
    expect(result.type).toBe('feedback/refine/fulfilled');
  });

  it.each([
    ['', 'empty'],
    ['   ', 'whitespace'],
  ])('rejects %s input without calling the API (%s)', async (input) => {
    const api = vi.fn();
    const store = createStore(api);

    const result = await dispatchRefine(store, input);

    expect(api).not.toHaveBeenCalled();
    expect((result.payload as RefineFeedbackFailure).code).toBe('InvalidRequest');
  });

  it('rejects an empty refined text with InvalidRefinedText', async () => {
    const api = vi.fn().mockResolvedValue({ data: { refinedText: '   ' } });
    const store = createStore(api);

    const result = await dispatchRefine(store, feedback);

    expect((result.payload as RefineFeedbackFailure).code).toBe('InvalidRefinedText');
  });

  it.each([
    [400, 'InvalidRequest', 'InvalidRequest'],
    [400, 'FeedbackTooLong', 'FeedbackTooLong'],
    [408, 'Timeout', 'Timeout'],
    [422, 'InvalidRefinedText', 'InvalidRefinedText'],
    [422, 'RefinedTextTooLong', 'RefinedTextTooLong'],
    [503, 'ServiceUnavailable', 'ServiceUnavailable'],
    [503, 'FeatureDisabled', 'FeatureDisabled'],
    [500, 'InternalError', 'InternalError'],
  ])('maps a %i %s response to the %s failure code', async (status, code, expected) => {
    const api = vi.fn().mockRejectedValue(axiosErrorWith(status, code, 'Feedback refinement failed.'));
    const store = createStore(api);

    const result = await dispatchRefine(store, feedback);

    expect((result.payload as RefineFeedbackFailure).code).toBe(expected);
  });

  it('falls back to the status code when the body carries no error code', async () => {
    const api = vi.fn().mockRejectedValue(axiosErrorWith(503));
    const store = createStore(api);

    const result = await dispatchRefine(store, feedback);

    expect((result.payload as RefineFeedbackFailure).code).toBe('ServiceUnavailable');
  });

  it('maps a network failure to NetworkError', async () => {
    const api = vi.fn().mockRejectedValue(new Error('Network Error'));
    const store = createStore(api);

    const result = await dispatchRefine(store, feedback);

    expect((result.payload as RefineFeedbackFailure).code).toBe('NetworkError');
  });

  it('never logs the Axios error object or the feedback', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined);
    const consoleLog = vi.spyOn(console, 'log').mockImplementation(() => undefined);
    const consoleWarn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    const api = vi.fn().mockRejectedValue(axiosErrorWith(500, 'InternalError'));
    const store = createStore(api);

    await store.dispatch(refineFeedback(feedback) as never);

    expect(consoleError).not.toHaveBeenCalled();
    expect(consoleLog).not.toHaveBeenCalled();
    expect(consoleWarn).not.toHaveBeenCalled();

    consoleError.mockRestore();
    consoleLog.mockRestore();
    consoleWarn.mockRestore();
  });

  it('does not leak feedback into the rejected payload', async () => {
    const api = vi.fn().mockRejectedValue(axiosErrorWith(500, 'InternalError', 'Feedback refinement failed.'));
    const store = createStore(api);

    const result = await dispatchRefine(store, feedback);

    expect(JSON.stringify(result.payload)).not.toContain('exclusions');
  });
});
