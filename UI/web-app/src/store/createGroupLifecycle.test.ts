// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { NewGroupType } from '../models/NewGroupType';
import reducer, {
  manageMembershipCreatedGroupDestinationLoadError,
  manageMembershipCreatedGroupDestinationLoading,
  resetCreateGroup, resetManageMembership, setSelectedDestination,
} from './manageMembership.slice';
import { createGroup } from './groups.api';
import { getGroupEndpoints } from './manageMembership.api';
import { DestinationType } from '../models/DestinationType';
import { setupStore } from './store';

const request = { groupName: 'Finance', groupAlias: 'Finance', groupType: NewGroupType.Security, description: '' };
const initial = reducer(undefined, { type: 'init' });

describe('Create group lifecycle', () => {
  it('does not reset a pending attempt or accept a stale result', () => {
    const pending = reducer(initial, createGroup.pending('current', request));
    expect(reducer(pending, resetCreateGroup())).toEqual(pending);
    expect(reducer(pending, resetManageMembership())).toEqual(pending);
    expect(reducer(pending, createGroup.fulfilled({ ok: true, statusCode: 200, groupId: 'stale-id' }, 'stale', request))).toEqual(pending);
  });

  it('keeps the known ID through destination-loading failure and recovery', () => {
    const pending = reducer(initial, createGroup.pending('current', request));
    const created = reducer(pending, createGroup.fulfilled({ ok: true, statusCode: 200, groupId: 'known-id' }, 'current', request));
    const loading = reducer(created, getGroupEndpoints.pending('load', 'known-id'));
    expect(loading.createdGroupDestinationLoading).toBe(true);
    const failed = reducer(loading, getGroupEndpoints.rejected(new Error('read failed'), 'load', 'known-id'));
    expect(failed.createdGroupId).toBe('known-id');
    expect(failed.selectedDestination?.id).toBe('known-id');
    expect(failed.createdGroupDestinationLoadError).toBe(true);
    expect(failed.createdGroupDestinationLoading).toBe(false);
    const recovered = reducer(failed, getGroupEndpoints.fulfilled(['SecurityGroup'], 'retry', 'known-id'));
    expect(recovered.createdGroupId).toBe('known-id');
    expect(recovered.createdGroupDestinationLoadError).toBe(false);
    expect(recovered.selectedDestination?.endpoints).toEqual(['SecurityGroup']);
  });

  it('does not turn existing-destination failures into create-flow failures', () => {
    const state = reducer(initial, getGroupEndpoints.rejected(new Error('read failed'), 'load', 'existing-id'));
    expect(state.createdGroupDestinationLoadError).not.toBe(true);
    expect(state.createdGroupId).toBeUndefined();
  });

  it.each(['pending', 'failed'] as const)('scopes a %s created-destination read to its selected group', outcome => {
    const store = setupStore();
    store.dispatch(createGroup.pending('current', request));
    store.dispatch(createGroup.fulfilled({ ok: true, statusCode: 200, groupId: 'created-id' }, 'current', request));
    store.dispatch(getGroupEndpoints.pending('load', 'created-id'));
    if (outcome === 'failed') {
      store.dispatch(getGroupEndpoints.rejected(new Error('read failed'), 'load', 'created-id'));
    }
    expect(manageMembershipCreatedGroupDestinationLoading(store.getState())).toBe(outcome === 'pending');
    expect(manageMembershipCreatedGroupDestinationLoadError(store.getState())).toBe(outcome === 'failed');

    store.dispatch(setSelectedDestination({ id: 'existing-id', name: 'Existing', type: DestinationType.GroupMembership }));
    store.dispatch(getGroupEndpoints.fulfilled(['SecurityGroup'], 'existing-load', 'existing-id'));
    expect(manageMembershipCreatedGroupDestinationLoading(store.getState())).toBe(false);
    expect(manageMembershipCreatedGroupDestinationLoadError(store.getState())).toBe(false);
    expect(store.getState().manageMembership.createdGroupId).toBe('created-id');

    store.dispatch(setSelectedDestination({ id: 'created-id', name: 'Finance', type: DestinationType.GroupMembership }));
    expect(manageMembershipCreatedGroupDestinationLoading(store.getState())).toBe(outcome === 'pending');
    expect(manageMembershipCreatedGroupDestinationLoadError(store.getState())).toBe(outcome === 'failed');
  });

  it('does not carry existing Exchange settings into the newly created destination or onboarding', () => {
    const groupSettings = { authorizedSenders: [], hiddenFromExchangeClients: true, welcomeMessageEnabled: true };
    const previous = {
      ...initial,
      groupSettings,
      selectedDestination: { id: 'previous-id', name: 'Previous', type: DestinationType.GroupMembership, groupSettings },
    };
    const pending = reducer(previous, createGroup.pending('current', request));
    const created = reducer(pending, createGroup.fulfilled({ ok: true, statusCode: 200, groupId: 'new-id' }, 'current', request));
    expect(created.groupSettings).toBeUndefined();
    expect(created.selectedDestination).toEqual({ id: 'new-id', name: 'Finance', type: DestinationType.GroupMembership });
    expect(JSON.parse(JSON.stringify({ groupSettings: created.groupSettings }))).toEqual({});
  });

  it('clears stale results only on a fresh, idle creation flow', () => {
    const uncertain = reducer(initial, createGroup.fulfilled({ ok: true, statusCode: 200 }, 'current', request));
    expect(uncertain.createGroupUncertain).toBe(true);
    const fresh = reducer(uncertain, resetCreateGroup());
    expect(fresh.createGroupUncertain).toBe(false);
    expect(fresh.createGroupErrorMessage).toBeUndefined();
  });
});
