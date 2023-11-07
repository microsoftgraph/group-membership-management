// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IAuthenticationService, TokenType } from '../services/auth';
import { Account } from '../models/Account';

export class OfflineAuthenticationService implements IAuthenticationService {

  // overridable properties
  public loginPromise: Promise<void> = Promise.resolve();
  public activeAccount: Account | undefined = {
    environment: 'testEnvironment',
    homeAccountId: 'testHomeAccountId',
    localAccountId: 'testLocalAccountId',
    name: 'testName',
    tenantId: 'testTenantId',
    username: 'testUsername',
    idToken: 'testIdToken',
    nativeAccountId: 'testNativeAccountId',
  };

  public tokens: { readonly [key in TokenType]: Promise<string> } = {
    [TokenType.Graph]: Promise.resolve('testGraphToken'),
    [TokenType.GMM]: Promise.resolve('testGmmToken'),
  };

  // mocked methods
  public loginAsync = async () => await this.loginPromise;
  public getActiveAccount = () => this.activeAccount;
  public getTokenAsync = (tokenType: TokenType) => this.tokens[tokenType];
}
