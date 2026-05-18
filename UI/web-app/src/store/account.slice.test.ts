// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import accountReducer, {
  setLoggedIn,
  setLoggingIn,
  selectAccount,
  selectAccountName,
  selectAccountUsername,
  selectLoggedIn,
  selectLoginError,
} from './account.slice';
import { loginAsync } from './account.api';

const initial = accountReducer(undefined, { type: '@@INIT' });

describe('account.slice — reducers', () => {
  it('setLoggedIn sets loggedIn', () => {
    expect(accountReducer(initial, setLoggedIn(true)).loggedIn).toBe(true);
  });

  it('setLoggingIn sets loggingIn', () => {
    expect(accountReducer(initial, setLoggingIn(true)).loggingIn).toBe(true);
  });
});

describe('account.slice — extraReducers', () => {
  it('loginAsync.fulfilled sets user', () => {
    const user = { name: 'Alice', username: 'alice@example.com' };
    const state = accountReducer(initial, loginAsync.fulfilled(user as any, 'req1', undefined as any));
    expect(state.user).toEqual(user);
  });

  it('loginAsync.rejected sets loginError', () => {
    const state = accountReducer(
      initial,
      { type: loginAsync.rejected.type, payload: 'Login failed' }
    );
    expect(state.loginError).toBe('Login failed');
  });
});

describe('account.slice — selectors', () => {
  const user = { name: 'Bob', username: 'bob@ex.com' };
  const root = { account: { ...initial, user, loggedIn: true, loginError: 'err' } } as any;

  it('selectAccount returns user', () => {
    expect(selectAccount(root)).toEqual(user);
  });

  it('selectAccountName returns name', () => {
    expect(selectAccountName(root)).toBe('Bob');
  });

  it('selectAccountUsername returns username', () => {
    expect(selectAccountUsername(root)).toBe('bob@ex.com');
  });

  it('selectLoggedIn returns loggedIn', () => {
    expect(selectLoggedIn(root)).toBe(true);
  });

  it('selectLoginError returns error', () => {
    expect(selectLoginError(root)).toBe('err');
  });

  it('selectAccountName returns undefined when no user', () => {
    expect(selectAccountName({ account: initial } as any)).toBeUndefined();
  });
});
