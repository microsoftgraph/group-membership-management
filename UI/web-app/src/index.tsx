// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
import React from "react";
import ReactDOM from "react-dom";
import { ThemeProvider } from "@fluentui/react";
import { Provider } from "react-redux";
import { store } from "./store";
import { BrowserRouter, Route, Routes } from "react-router-dom";
import { JobsPage, JobDetailsPage, OwnerPage } from "./pages";
import { App } from "./App";
import { MsalProvider } from "@azure/msal-react";
import { initializeIcons } from '@fluentui/font-icons-mdl2';



import {
  PublicClientApplication,
  EventType,
  EventMessage,
  AuthenticationResult,
} from "@azure/msal-browser";
import { msalConfig } from "./authConfig";


export const msalInstance = new PublicClientApplication(msalConfig);

const accounts = msalInstance.getAllAccounts();
if (accounts.length > 0) {
  msalInstance.setActiveAccount(accounts[0]);
}

msalInstance.addEventCallback((event: EventMessage) => {
  if (event.eventType === EventType.LOGIN_SUCCESS && event.payload) {
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

  document.getElementById("root")
);