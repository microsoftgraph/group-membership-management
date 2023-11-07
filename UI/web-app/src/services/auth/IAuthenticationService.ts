// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { TokenType } from './TokenType';
import { User } from '../../models/User';

export interface IAuthenticationService {
  loginAsync: () => Promise<void>;
  getActiveAccount(): User | undefined;
  getTokenAsync: (tokenType: TokenType) => Promise<string>;
}
