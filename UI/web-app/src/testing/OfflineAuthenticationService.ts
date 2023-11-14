// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { IAuthenticationService, TokenType } from '../services/auth';
import { User } from '../models/User';

export class OfflineAuthenticationService implements IAuthenticationService {
  // overridable properties
  public loginPromise: Promise<void> = Promise.resolve();
  public user: User | undefined = {
    id: 'testLocalAccountId',
    name: 'testName',
  };

  public tokens: { readonly [key in TokenType]: Promise<string> } = {
    [TokenType.Graph]: Promise.resolve('testGraphToken'),
    [TokenType.GMM]: Promise.resolve('testGmmToken'),
  };

  // mocked methods
  public loginAsync = async () => await this.loginPromise;
  public getActiveAccount = () => this.user;
  public getTokenAsync = (tokenType: TokenType) => this.tokens[tokenType];
}
