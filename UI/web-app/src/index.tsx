// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ThemeProvider, initializeIcons } from '@fluentui/react';
import React from 'react';
import ReactDOM from 'react-dom';
import { ApplicationInsights } from '@microsoft/applicationinsights-web';
import { ReactPlugin } from '@microsoft/applicationinsights-react-js';
import { Provider } from 'react-redux';
import { BrowserRouter, Route, Routes } from 'react-router-dom';

import { App } from './App';
import { AdminConfig, JobsPage, JobDetails, OwnerPage, ManageMembership } from './pages';
import { store } from './store';

const connectionString = process.env.REACT_APP_APPINSIGHTS_CONNECTIONSTRING;
if (!connectionString || connectionString === '') {
  console.warn('App Insights Connection String is not set in pipeline variables. App Insights will not be enabled.');
} else {
  const reactPlugin = new ReactPlugin();
  const appInsights = new ApplicationInsights({
    config: {
      connectionString,
      autoTrackPageVisitTime: true,
      enableAjaxErrorStatusText: true,
      enableAjaxPerfTracking: true,
      enableCorsCorrelation: true,
      enableAutoRouteTracking: true,
      enablePerfMgr: true,
      namePrefix: 'GMM_AI_',
      extensions: [reactPlugin],
      extensionConfig: {
        [reactPlugin.identifier]: {
          // history is set to null because we are using enableAutoRouteTracking.
          // history is used to track page views that do not update the browser url.
          // enabling both will cause app insights to report duplicate page views.
          history: null,
        },
      },
    },
  });
  appInsights.loadAppInsights();
}

initializeIcons();
ReactDOM.render(
  <ThemeProvider>
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
  </ThemeProvider>,

  document.getElementById('root')
);
