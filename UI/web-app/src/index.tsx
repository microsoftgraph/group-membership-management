// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
import {
  PublicClientApplication,
  EventType,
  type EventMessage,
  type AuthenticationResult,
} from '@azure/msal-browser';
import { MsalProvider } from '@azure/msal-react';
import { ThemeProvider, initializeIcons } from '@fluentui/react';
import React from 'react';
import ReactDOM from 'react-dom';
import { Provider } from 'react-redux';
import { BrowserRouter, Route, Routes } from 'react-router-dom';

import { App } from './App';
import { msalConfig } from './authConfig';
import { JobsPage, JobDetailsPage, OwnerPage } from './pages';
import { store } from './store';

export const msalInstance = new PublicClientApplication(msalConfig);

const accounts = msalInstance.getAllAccounts();
if (accounts.length > 0) {
  msalInstance.setActiveAccount(accounts[0]);
}

msalInstance.addEventCallback((event: EventMessage) => {
  if (event.eventType === EventType.LOGIN_SUCCESS && event.payload != null) {
    const payload = event.payload as AuthenticationResult;
    const account = payload.account;
    msalInstance.setActiveAccount(account);
  }
});

initializeIcons();

ReactDOM.render(
  <ThemeProvider>
    <MsalProvider instance={msalInstance}>
      <React.StrictMode>
        <Provider store={store}>
          <BrowserRouter>
            <Routes>
              <Route path="" element={<App />}>
                <Route path="/" element={<JobsPage />} />
                <Route path="/JobDetailsPage" element={<JobDetailsPage />} />
                <Route path="/OwnerPage" element={<OwnerPage />} />
              </Route>
            </Routes>
          </BrowserRouter>
        </Provider>
      </React.StrictMode>
    </MsalProvider>
  </ThemeProvider>,

  document.getElementById('root')
);
