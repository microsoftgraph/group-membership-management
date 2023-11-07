// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { TokenType } from './TokenType';
import { Account } from '../../models/Account';

export interface IAuthenticationService {
  loginAsync: () => Promise<void>;
  getActiveAccount(): Account | undefined;
  getTokenAsync: (tokenType: TokenType) => Promise<string>;
}
