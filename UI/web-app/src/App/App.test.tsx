// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { screen } from '@testing-library/react';

import { App } from './App';
import { renderWithProviders } from '../testing';
import { OfflineAuthenticationService } from '../testing/OfflineAuthenticationService';

test('renders header after login', () => {
  const authenticationService = new OfflineAuthenticationService();
  renderWithProviders(<App />, { serviceMocks: { authenticationService } });
  expect(screen.getByText(/Membership Management/i)).toBeInTheDocument();
});
