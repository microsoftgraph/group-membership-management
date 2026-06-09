import React from 'react';
import { act, fireEvent, screen } from '@testing-library/react';
import { GroupSetting } from './GroupSetting';
import { renderWithProviders } from '../../testing/renderWithProviders';
import manageMembershipReducer, { setGroupSettings } from '../../store/manageMembership.slice';

const getManageMembershipState = () =>
  manageMembershipReducer(undefined, { type: 'test/init' });

describe('GroupSetting', () => {
  it('defaults the hidden from Exchange toggle to checked when no settings exist', () => {
    renderWithProviders(<GroupSetting />);

    const toggles = screen.getAllByRole('switch');

    expect(toggles[0]).toBeChecked();
    expect(toggles[1]).toBeDisabled();
  });

  it('persists hiddenFromExchangeClients changes when toggled', () => {
    const { store } = renderWithProviders(<GroupSetting />);

    const toggles = screen.getAllByRole('switch');
    fireEvent.click(toggles[0]);

    expect(store.getState().manageMembership.groupSettings?.hiddenFromExchangeClients).toBe(false);
  });

  it('honors existing hiddenFromExchangeClients setting from the store', () => {
    const { store } = renderWithProviders(<GroupSetting />, {
      preloadedState: {
        manageMembership: {
          ...getManageMembershipState(),
          groupSettings: {
            authorizedSenders: [],
            hiddenFromExchangeClients: false,
            welcomeMessageEnabled: false,
          },
        },
      },
    });

    const toggles = screen.getAllByRole('switch');
    expect(toggles[0]).not.toBeChecked();

    act(() => {
      store.dispatch(setGroupSettings({
        authorizedSenders: [],
        hiddenFromExchangeClients: true,
        welcomeMessageEnabled: false,
      }));
    });

    expect(screen.getAllByRole('switch')[0]).toBeChecked();
  });
});