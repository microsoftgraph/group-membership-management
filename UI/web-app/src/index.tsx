// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ThemeProvider, initializeIcons } from '@fluentui/react';
import React from 'react';
import ReactDOM from 'react-dom';
import { Provider } from 'react-redux';
import { BrowserRouter, Route, Routes } from 'react-router-dom';

import { App } from './App';
import { LocalizationProvider } from './localization';
import { AdminConfig, JobsPage, JobDetails, OwnerPage, ManageMembership } from './pages';
import { store } from './store';

initializeIcons();
ReactDOM.render(
  <ThemeProvider>
    <LocalizationProvider>
      <React.StrictMode>
        <Provider store={store}>
          <BrowserRouter>
            <Routes>
              <Route path="" element={<App />}>
                <Route path="/" element={<JobsPage />} />
                <Route path="/JobDetails" element={<JobDetails />} />
                <Route path="/OwnerPage" element={<OwnerPage />} />
                <Route path="/AdminConfig" element={<AdminConfig />} />
                <Route path="/ManageMembership" element={<ManageMembership />} />
              </Route>
            </Routes>
          </BrowserRouter>
        </Provider>
      </React.StrictMode>
    </LocalizationProvider>
  </ThemeProvider>,

  document.getElementById('root')
);
