// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';

import { renderWithProviders } from '../../testing/renderWithProviders';
import { defaultStrings } from '../../services/localization';
import { OwnerBase } from './Owner.base';
import { OwnerState } from '../../store/owner.slice';

const createAuthenticationServiceMock = () => ({
  loginAsync: jest.fn().mockResolvedValue(undefined),
  getActiveAccount: jest.fn().mockReturnValue({
    id: 'test-account-id',
    name: 'Test User',
    username: 'test.user@example.com',
  }),
  getTokenAsync: jest.fn().mockResolvedValue('test-token'),
});

const renderComponent = (ownerOverrides?: Partial<OwnerState>) => {
  const authenticationServiceMock = createAuthenticationServiceMock();

  return {
    authenticationServiceMock,
    ...renderWithProviders(<OwnerBase />, {
      preloadedState: {
        localization: {
          language: 'en',
          strings: defaultStrings,
        },
        owner: {
          loading: false,
          status: '',
          ...ownerOverrides,
        },
      },
      serviceMocks: {
        authenticationService: authenticationServiceMock,
      },
    }),
  };
};

describe('OwnerBase', () => {
  let fetchMock: jest.Mock;

  beforeEach(() => {
    jest.clearAllMocks();
    fetchMock = jest.fn().mockResolvedValue({
      ok: true,
      status: 204,
      statusText: 'No Content',
    });
    global.fetch = fetchMock as unknown as typeof fetch;
  });

  it('renders localized header and input', () => {
    renderComponent();

    expect(
      screen.getByText(defaultStrings.groupIdHeader)
    ).toBeInTheDocument();
    expect(
      screen.getByPlaceholderText(defaultStrings.groupIdPlaceHolder)
    ).toBeInTheDocument();
  });

  it('dispatches addOwner when the submit button is clicked', async () => {
    const { authenticationServiceMock } = renderComponent();

    fireEvent.change(
      screen.getByLabelText(defaultStrings.groupIdPlaceHolder),
      { target: { value: 'test-group' } }
    );
    fireEvent.click(screen.getByText(defaultStrings.okButton));

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1));

    expect(authenticationServiceMock.getTokenAsync).toHaveBeenCalled();
    expect(fetchMock).toHaveBeenCalledWith(
      'https://graph.microsoft.com/v1.0/groups/test-group/owners/$ref/',
      expect.objectContaining({ method: 'POST' })
    );
    expect(
      screen.getByText(defaultStrings.addOwner204Message)
    ).toBeInTheDocument();
  });

  it.each([
    ['false 403 Forbidden', defaultStrings.addOwner403Message],
    ['false 400 Bad Request', defaultStrings.addOwner400Message],
    ['true 204 No Content', defaultStrings.addOwner204Message],
    ['unexpected', defaultStrings.addOwnerErrorMessage],
  ])('renders status message for %s', (status, expectedMessage) => {
    renderComponent({ status });

    expect(screen.getByText(expectedMessage)).toBeInTheDocument();
  });
});
