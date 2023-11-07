// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  AuthenticationResult,
  Configuration,
  PublicClientApplication,
  RedirectRequest,
  SilentRequest,
} from '@azure/msal-browser';
import { IAuthenticationService } from './IAuthenticationService';
import { TokenType } from './TokenType';
import { Account } from '../../models/Account';

const gmmTokenRequest: SilentRequest = {
  scopes: [`api://${process.env.REACT_APP_AAD_API_APP_CLIENT_ID}/user_impersonation`],
};

// The user will consent to both scopes, but only receive a token for the first.
// This will prevent the user from having to consent again when a token is retrieve for the api.
const loginRequest: RedirectRequest = {
  scopes: ['User.Read'],
  extraScopesToConsent: [`api://${process.env.REACT_APP_AAD_API_APP_CLIENT_ID}/user_impersonation`],
};

const graphTokenRequest: SilentRequest = {
  scopes: ['User.Read'],
};

export class MsalAuthenticationService implements IAuthenticationService {
  private _initialized = false;
  private _msalInstance: PublicClientApplication;
  private _msalConfig: Configuration = {
    auth: {
      clientId: `${process.env.REACT_APP_AAD_UI_APP_CLIENT_ID}`,
      authority: 'https://login.microsoftonline.com/organizations',
      redirectUri: '/',
      postLogoutRedirectUri: '/',
    },
  };

  constructor() {
    this._msalInstance = new PublicClientApplication(this._msalConfig);
  }

  public async loginAsync(): Promise<void> {
    let authenticationResult: AuthenticationResult | null = null;

    if (!this._initialized) {
      await this._msalInstance.initialize();

      // handle redirect promise if we are loading after a login redirect
      authenticationResult = await this._msalInstance.handleRedirectPromise();
      this._initialized = true;
    }

    if (authenticationResult !== null) {
      // if we are loading after a redirect, check to see if we are logged in
      if (authenticationResult.account) {
        this._msalInstance.setActiveAccount(authenticationResult.account);
      } else {
        // if we are not logged in, start the login flow
        await this._msalInstance.loginRedirect(loginRequest);
      }
    } else {
      const accounts = this._msalInstance.getAllAccounts();

      // if we are not logged in, start the login flow
      if (accounts?.length === 0) {
        await this._msalInstance.loginRedirect(loginRequest);
      } else {
        // choose first account if there are multiple
        this._msalInstance.setActiveAccount(accounts[0]);
      }
    }
  }

  public getActiveAccount(): Account | undefined {
    const activeAccount = this._msalInstance.getActiveAccount();
    return !activeAccount ? undefined : { ...activeAccount };
  }

  public async getTokenAsync(tokenType: TokenType): Promise<string> {
    const request = tokenType === TokenType.GMM ? gmmTokenRequest : graphTokenRequest;

    const account = this.getActiveAccount();
    const tokenResponse = await this._msalInstance.acquireTokenSilent({
      ...request,
      account,
    });

    return tokenResponse.accessToken;
  }
}
