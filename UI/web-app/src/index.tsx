// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ThemeProvider, initializeIcons } from '@fluentui/react';
import React from 'react';
import ReactDOM from 'react-dom';
import { ApplicationInsights } from '@microsoft/applicationinsights-web';
import { ReactPlugin } from '@microsoft/applicationinsights-react-js';
import { Provider } from 'react-redux';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import './index.css';

import { App } from './App';
import { AdminConfig, JobsPage, JobDetails, OwnerPage, ManageMembership, NotFound } from './pages';
import { MaintenanceCheckWrapper } from './components/MaintenanceCheckWrapper';
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

// Wrap components that should respect maintenance mode
const JobsPageWithMaintenanceCheck = MaintenanceCheckWrapper(JobsPage);
const JobDetailsWithMaintenanceCheck = MaintenanceCheckWrapper(JobDetails);
const OwnerPageWithMaintenanceCheck = MaintenanceCheckWrapper(OwnerPage);
const ManageMembershipWithMaintenanceCheck = MaintenanceCheckWrapper(ManageMembership);
const NotFoundWithMaintenanceCheck = MaintenanceCheckWrapper(NotFound);

ReactDOM.render(
  <ThemeProvider>
    <React.StrictMode>
      <Provider store={store}>
        <BrowserRouter basename="/">
          <Routes>
            <Route path="" element={<App />}>
              <Route path="/" element={<JobsPageWithMaintenanceCheck />} />
              <Route path="/JobDetails/:jobId" element={<JobDetailsWithMaintenanceCheck />} />
              <Route path="/Groups/:groupId" element={<JobDetailsWithMaintenanceCheck />} />
              <Route path="/Groups/:groupId/Channels/:channelId" element={<JobDetailsWithMaintenanceCheck />} />
              <Route path="/OwnerPage" element={<OwnerPageWithMaintenanceCheck />} />
              <Route path="/Admin" element={<AdminConfig />} />
              <Route path="/ManageMembership" element={<ManageMembershipWithMaintenanceCheck />} />
              <Route path="/ManageMembership/:jobId" element={<ManageMembershipWithMaintenanceCheck />} />
              <Route path="/NotFound" element={<NotFoundWithMaintenanceCheck />} />
            </Route>
          </Routes>
        </BrowserRouter>
      </Provider>
    </React.StrictMode>
  </ThemeProvider>,

  document.getElementById('root')
);
