// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { beforeEach, describe, expect, it, vi } from 'vitest';
import { InteractionRequiredAuthError } from '@azure/msal-browser';
import { MsalAuthenticationService } from './MsalAuthenticationService';
import { TokenType } from './TokenType';

// @azure/msal-browser is mocked in setupTests.ts (PublicClientApplication +
// InteractionRequiredAuthError). Each test constructs a fresh service so the
// internal interactive-auth guard is reset.

const flushMicrotasks = () => new Promise((resolve) => setTimeout(resolve, 0));

describe('MsalAuthenticationService.getTokenAsync', () => {
  let service: MsalAuthenticationService;
  let msalInstance: any;

  beforeEach(() => {
    service = new MsalAuthenticationService();
    msalInstance = (service as any)._msalInstance;
  });

  it('returns the silently acquired token and does not redirect', async () => {
    msalInstance.acquireTokenSilent = vi.fn().mockResolvedValue({ accessToken: 'silent-token' });

    const token = await service.getTokenAsync(TokenType.GMM);

    expect(token).toBe('silent-token');
    expect(msalInstance.acquireTokenRedirect).not.toHaveBeenCalled();
  });

  it('falls back to an interactive redirect when the cached session needs re-auth', async () => {
    msalInstance.acquireTokenSilent = vi
      .fn()
      .mockRejectedValue(new InteractionRequiredAuthError('interaction_required'));

    let resolved = false;
    // The returned promise must never resolve, because the page is navigating to
    // AAD; callers must not proceed token-less.
    service.getTokenAsync(TokenType.GMM).then(() => {
      resolved = true;
    });

    await flushMicrotasks();

    expect(msalInstance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
    expect(resolved).toBe(false);
  });

  it('only triggers a single redirect when several token requests fail concurrently', async () => {
    msalInstance.acquireTokenSilent = vi
      .fn()
      .mockRejectedValue(new InteractionRequiredAuthError('interaction_required'));

    service.getTokenAsync(TokenType.GMM);
    service.getTokenAsync(TokenType.Graph);
    service.getTokenAsync(TokenType.GMM);

    await flushMicrotasks();

    expect(msalInstance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
  });

  it('rethrows non-interaction errors without redirecting', async () => {
    const unexpected = new Error('network down');
    msalInstance.acquireTokenSilent = vi.fn().mockRejectedValue(unexpected);

    await expect(service.getTokenAsync(TokenType.GMM)).rejects.toBe(unexpected);
    expect(msalInstance.acquireTokenRedirect).not.toHaveBeenCalled();
  });

  it('clears the interactive-auth guard when starting the redirect fails, allowing a later retry', async () => {
    msalInstance.acquireTokenSilent = vi
      .fn()
      .mockRejectedValue(new InteractionRequiredAuthError('interaction_required'));
    msalInstance.acquireTokenRedirect = vi
      .fn()
      .mockRejectedValueOnce(new Error('interaction_in_progress'))
      .mockResolvedValue(undefined);

    // The first attempt surfaces the redirect-initiation failure instead of
    // hanging silently.
    await expect(service.getTokenAsync(TokenType.GMM)).rejects.toThrow('interaction_in_progress');

    // Because the guard was reset, a subsequent token request can retry the
    // redirect rather than being stuck on a never-resolving promise.
    service.getTokenAsync(TokenType.GMM);
    await flushMicrotasks();

    expect(msalInstance.acquireTokenRedirect).toHaveBeenCalledTimes(2);
  });

  it('rejects all concurrent callers (none left hanging) when starting the redirect fails', async () => {
    msalInstance.acquireTokenSilent = vi
      .fn()
      .mockRejectedValue(new InteractionRequiredAuthError('interaction_required'));
    msalInstance.acquireTokenRedirect = vi
      .fn()
      .mockRejectedValue(new Error('interaction_in_progress'));

    // Three token requests fail concurrently and share a single redirect
    // attempt. When that attempt fails to start, every caller must reject —
    // none may be stranded on the never-resolving success promise.
    const settled = await Promise.allSettled([
      service.getTokenAsync(TokenType.GMM),
      service.getTokenAsync(TokenType.Graph),
      service.getTokenAsync(TokenType.GMM),
    ]);

    expect(settled.map((result) => result.status)).toEqual(['rejected', 'rejected', 'rejected']);
    expect(msalInstance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
  });

  it('forwards a claims challenge to the interactive redirect', async () => {
    const claimsChallenge = '{"access_token":{"nbf":{"essential":true}}}';
    const interactionError = new InteractionRequiredAuthError('interaction_required');
    (interactionError as unknown as { claims: string }).claims = claimsChallenge;
    msalInstance.acquireTokenSilent = vi.fn().mockRejectedValue(interactionError);

    service.getTokenAsync(TokenType.GMM);
    await flushMicrotasks();

    expect(msalInstance.acquireTokenRedirect).toHaveBeenCalledTimes(1);
    expect(msalInstance.acquireTokenRedirect).toHaveBeenCalledWith(
      expect.objectContaining({ claims: claimsChallenge })
    );
  });

  it('throws when there is no active account', async () => {
    msalInstance.getActiveAccount = vi.fn().mockReturnValue(null);

    await expect(service.getTokenAsync(TokenType.GMM)).rejects.toThrow('No active account');
  });
});
