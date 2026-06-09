import React from 'react';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { WelcomeName } from './WelcomeName';

describe('WelcomeName', () => {
  it('greets the user when a name is available', () => {
    const { container } = renderWithProviders(<WelcomeName />, {
      preloadedState: {
        account: {
          user: {
            id: 'user-123',
            name: 'Ada Lovelace',
            username: 'ada@example.com',
          },
          loggedIn: true,
          loggingIn: false,
          loginError: undefined,
        },
      },
    });

    expect(container).toHaveTextContent('Ada Lovelace');
  });

  it('renders nothing when the user name is missing', () => {
    const { container } = renderWithProviders(<WelcomeName />);

    expect(container.firstChild).toBeNull();
  });
});
