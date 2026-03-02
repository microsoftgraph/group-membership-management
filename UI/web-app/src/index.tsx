// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { ThemeProvider, initializeIcons, loadTheme, createTheme } from '@fluentui/react';
import React from 'react';
import ReactDOM from 'react-dom';
import { ApplicationInsights } from '@microsoft/applicationinsights-web';
import { ReactPlugin } from '@microsoft/applicationinsights-react-js';
import { Provider, useSelector } from 'react-redux';
import { BrowserRouter, Route, Routes } from 'react-router-dom';
import './index.css';

import { App } from './App';
import { AdminConfig, JobsPage, JobDetails, OwnerPage, ManageMembership, NotFound } from './pages';
import { MaintenanceCheckWrapper } from './pages/Maintenance/MaintenanceCheckWrapper';
import { store } from './store';
import { selectIsDarkMode } from './store/theme.slice';

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

// ThemedApp component that applies theme based on Redux state
const ThemedApp: React.FC = () => {
  const isDarkMode = useSelector(selectIsDarkMode);
  
  React.useEffect(() => {
    const theme = createTheme({
      palette: isDarkMode ? {
        themePrimary: '#0078d4',
        themeLighterAlt: '#eff6fc',
        themeLighter: '#deecf9',
        themeLight: '#c7e0f4',
        themeTertiary: '#71afe5',
        themeSecondary: '#2b88d8',
        themeDarkAlt: '#106ebe',
        themeDark: '#005a9e',
        themeDarker: '#004578',
        neutralLighterAlt: '#1c1c1c',
        neutralLighter: '#252525',
        neutralLight: '#343434',
        neutralQuaternaryAlt: '#3d3d3d',
        neutralQuaternary: '#454545',
        neutralTertiaryAlt: '#656565',
        neutralTertiary: '#c8c8c8',
        neutralSecondary: '#d0d0d0',
        neutralSecondaryAlt: '#d0d0d0',
        neutralPrimaryAlt: '#dadada',
        neutralPrimary: '#ffffff',
        neutralDark: '#f4f4f4',
        black: '#f8f8f8',
        white: '#0b0b0b',
      } : undefined,
    });
    loadTheme(theme);
  }, [isDarkMode]);

  return (
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
  );
};

ReactDOM.render(
  <ThemeProvider>
    <React.StrictMode>
      <Provider store={store}>
        <ThemedApp />
      </Provider>
    </React.StrictMode>
  </ThemeProvider>,

  document.getElementById('root')
);
