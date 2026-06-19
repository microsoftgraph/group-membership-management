// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it, vi } from 'vitest';
import { fetchServiceStatus } from './operations.api';
import { ServiceStatuses } from '../models/ServiceStatuses';

// Drives the fetchServiceStatus thunk with a stubbed gmmApi that rejects with
// the supplied error, and returns the resulting (rejected) redux action so the
// auth-vs-service classification can be asserted directly.
const classifyFailure = async (rejectedError: unknown) => {
  const extra = {
    apis: {
      gmmApi: {
        operationsApi: {
          fetchServiceStatus: vi.fn().mockRejectedValue(rejectedError),
        },
      },
    },
  } as any;

  return await fetchServiceStatus()(vi.fn() as any, vi.fn() as any, extra);
};

describe('fetchServiceStatus — failure classification', () => {
  it('classifies an MSAL token-flow error (string errorCode) as auth', async () => {
    // getTokenAsync rethrows MSAL auth-layer errors such as interaction_in_progress.
    const msalError = Object.assign(new Error('interaction_in_progress: a redirect is already in progress'), {
      errorCode: 'interaction_in_progress',
      name: 'BrowserAuthError',
    });

    const action = await classifyFailure(msalError);

    expect(action.type).toBe('operations/fetchServiceStatus/rejected');
    expect(action.payload).toBe('auth');
  });

  it('classifies a real @azure/msal-browser AuthError as auth (pins the errorCode contract the fix relies on)', async () => {
    // The classifier identifies MSAL token-flow failures by their string
    // `errorCode`. Drive it with the real library error class (not a
    // hand-crafted shape) so this guard fails if a future MSAL upgrade ever
    // drops that contract — which would otherwise silently re-route token
    // errors back to the maintenance page. setupTests.ts mocks
    // @azure/msal-browser, so importActual is required to reach the real class.
    const { BrowserAuthError } = await vi.importActual<typeof import('@azure/msal-browser')>('@azure/msal-browser');
    const realMsalError = new BrowserAuthError('interaction_in_progress');
    expect(realMsalError).toBeInstanceOf(Error);
    expect(typeof (realMsalError as { errorCode?: unknown }).errorCode).toBe('string');

    const action = await classifyFailure(realMsalError);

    expect(action.type).toBe('operations/fetchServiceStatus/rejected');
    expect(action.payload).toBe('auth');
  });

  it('classifies a 401 AxiosError as auth', async () => {
    const unauthorized = Object.assign(new Error('Unauthorized'), {
      isAxiosError: true,
      response: { status: 401 },
    });

    expect((await classifyFailure(unauthorized)).payload).toBe('auth');
  });

  it('classifies a 403 AxiosError as auth', async () => {
    const forbidden = Object.assign(new Error('Forbidden'), {
      isAxiosError: true,
      response: { status: 403 },
    });

    expect((await classifyFailure(forbidden)).payload).toBe('auth');
  });

  it('classifies a missing active account as auth', async () => {
    const action = await classifyFailure(new Error('No active account. Please call loginAsync first.'));

    expect(action.payload).toBe('auth');
  });

  it('classifies a 5xx AxiosError as service', async () => {
    const serviceUnavailable = Object.assign(new Error('Service Unavailable'), {
      isAxiosError: true,
      response: { status: 503 },
    });

    expect((await classifyFailure(serviceUnavailable)).payload).toBe('service');
  });

  it('classifies an unexpected error as service (fail safe to maintenance)', async () => {
    expect((await classifyFailure(new Error('unexpected boom'))).payload).toBe('service');
  });

  it('resolves to fulfilled with the status when the call succeeds', async () => {
    const extra = {
      apis: {
        gmmApi: {
          operationsApi: {
            fetchServiceStatus: vi.fn().mockResolvedValue(ServiceStatuses.Running),
          },
        },
      },
    } as any;

    const action = await fetchServiceStatus()(vi.fn() as any, vi.fn() as any, extra);

    expect(action.type).toBe('operations/fetchServiceStatus/fulfilled');
    expect(action.payload).toBe(ServiceStatuses.Running);
  });
});
