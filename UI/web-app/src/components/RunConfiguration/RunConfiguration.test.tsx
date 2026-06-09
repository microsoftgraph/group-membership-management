import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { RunConfiguration } from './RunConfiguration';
import { renderWithProviders } from '../../testing/renderWithProviders';
import manageMembershipReducer from '../../store/manageMembership.slice';
import rolesReducer from '../../store/roles.slice';
import type { RootState } from '../../store';

const getManageMembershipState = (): RootState['manageMembership'] =>
  manageMembershipReducer(undefined, { type: 'test/init' });

const getRolesState = (): RootState['roles'] =>
  rolesReducer(undefined, { type: 'test/init' });

describe('RunConfiguration', () => {
  it('shows the date picker when the requested date option is selected', () => {
    const baseManageMembership = getManageMembershipState();
    const preloadedState: Partial<RootState> = {
      manageMembership: {
        ...baseManageMembership,
        startDateOption: 'RequestedDate',
        startDate: new Date('2024-01-01').toISOString(),
      },
      roles: {
        ...getRolesState(),
        isJobOwnerWriter: true,
      },
    };

    const { store } = renderWithProviders(<RunConfiguration />, { preloadedState });
    const strings = store.getState().localization.strings;

    expect(
      screen.getByLabelText(strings.ManageMembership.labels.selectRequestedStartDate)
    ).toBeInTheDocument();
  });

  it('disables threshold controls when choosing to skip automatic sync protection', () => {
    const preloadedState: Partial<RootState> = {
      manageMembership: {
        ...getManageMembershipState(),
      },
      roles: {
        ...getRolesState(),
        isJobOwnerWriter: true,
      },
    };

    const { store } = renderWithProviders(<RunConfiguration />, { preloadedState });
    const strings = store.getState().localization.strings;

    fireEvent.click(screen.getByRole('radio', { name: strings.no }));

    const state = store.getState().manageMembership;
    expect(state.useThresholdLimits).toBe('No');
    expect(state.showIncreaseDropdown).toBe(false);
    expect(state.showDecreaseDropdown).toBe(false);
    expect(state.newJob.thresholdPercentageForAdditions).toBe(-1);
    expect(state.newJob.thresholdPercentageForRemovals).toBe(-1);
    expect(screen.getByRole('status')).toBeInTheDocument();
  });

  it('hides the increase threshold dropdown when its checkbox is cleared', () => {
    const baseManageMembership = getManageMembershipState();
    const preloadedState: Partial<RootState> = {
      manageMembership: {
        ...baseManageMembership,
        newJob: {
          ...baseManageMembership.newJob,
          thresholdPercentageForAdditions: 70,
        },
      },
      roles: {
        ...getRolesState(),
        isJobOwnerWriter: true,
      },
    };

    const { store } = renderWithProviders(<RunConfiguration />, { preloadedState });
    const strings = store.getState().localization.strings;

    fireEvent.click(screen.getByRole('checkbox', { name: strings.ManageMembership.labels.increase }));

    const state = store.getState().manageMembership;
    expect(state.showIncreaseDropdown).toBe(false);
    expect(state.newJob.thresholdPercentageForAdditions).toBe(100);

    expect(screen.getByTitle(strings.ManageMembership.labels.increase)).toHaveStyle({ visibility: 'hidden' });
  });
});