// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { act, screen, waitFor } from '@testing-library/react';
import { createMemoryRouter, Outlet, RouterProvider } from 'react-router-dom';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { createGroup } from '../../store/groups.api';
import { NewGroupType } from '../../models/NewGroupType';
import { PendingGroupCreationGuard } from './PendingGroupCreationGuard';

const request = { groupName: 'Finance', groupAlias: 'Finance', groupType: NewGroupType.Microsoft365, description: '' };

describe('Pending group navigation', () => {
  it('blocks browser-history and programmatic exits, then discards blocked navigation', async () => {
    const router = createMemoryRouter([{
      element: <><PendingGroupCreationGuard /><Outlet /></>,
      children: [
        { path: '/', element: <div>Dashboard</div> },
        { path: '/ManageMembership', element: <div>Creation form</div> },
      ],
    }], { initialEntries: ['/', '/ManageMembership'], initialIndex: 1 });
    const { store } = renderWithProviders(<RouterProvider router={router} />);
    act(() => { store.dispatch(createGroup.pending('request', request)); });

    await act(async () => { await router.navigate('/'); });
    expect(screen.getByText('Creation form')).toBeInTheDocument();
    act(() => { router.navigate(-1); });
    await waitFor(() => expect(router.state.location.pathname).toBe('/ManageMembership'));

    const unload = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(unload);
    expect(unload.defaultPrevented).toBe(true);
    act(() => {
      store.dispatch(createGroup.rejected(null, 'request', request, { kind: 'rejected', statusCode: 400 }));
    });
    expect(screen.getByText('Creation form')).toBeInTheDocument();
    const completedUnload = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(completedUnload);
    expect(completedUnload.defaultPrevented).toBe(false);
    await act(async () => { await router.navigate('/'); });
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
  });
});
