// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { describe, expect, it } from 'vitest';
import manageMembershipReducer, {
  setHasChanges,
  setCurrentStep,
  setSelectedDestination,
  setDestinationEndpoints,
  setNewJobQuery,
  setNewJobRequestor,
  setNewJobStartDate,
  setNewJobThresholdPercentageForAdditions,
  setNewJobThresholdPercentageForRemovals,
  setUseThresholdLimits,
  setStartDateOption,
  setShowIncreaseDropdown,
  setShowDecreaseDropdown,
  resetManageMembership,
  setIsAdvancedView,
  setAdvancedViewQueryRaw,
  applyAdvancedViewQuery,
  setSourceParts,
  addSourcePart,
  updateSourcePartType,
  updateSourcePart,
  copySourcePart,
  deleteSourcePart,
  clearSourceParts,
  setIsEditingExistingJob,
  setCreatedGroupName,
  setCreateGroupErrorMessage,
  setBusinessJustification,
  setGroupSettings,
  clearGroupMembers,
  setIsMissingAndOrOperator,
  setIsAdvancedQueryValid,
  manageMembershipIsToggleEnabled,
  areAllSourcePartsValid,
  manageMembershipIsGroupReadyForOnboarding,
  buildCompositeQuery,
  ManageMembershipState,
} from './manageMembership.slice';
import {
  getGroupOnboardingStatus,
  getChannelOnboardingStatus,
  getGroupEndpoints,
  getGroupOwners,
  getGroupMembers,
  searchDestinations,
  searchChannels,
} from './manageMembership.api';
import { createGroup } from './groups.api';
import { SourcePartType } from '../models/SourcePartType';
import { OnboardingStatus } from '../models/GroupOnboardingStatus';
import type { ISourcePart } from '../models/ISourcePart';

const initial: ManageMembershipState = manageMembershipReducer(undefined, { type: '@@INIT' });

const makeSourcePart = (id: string, type = SourcePartType.GroupMembership, title = 'T'): ISourcePart => ({
  id,
  title,
  isNew: false,
  isExpanded: false,
  query: { type, source: '12345678-1234-1234-1234-123456789abc' } as any,
});

const buildRoot = (overrides: Partial<ManageMembershipState> = {}) =>
  ({ manageMembership: { ...initial, ...overrides } } as any);

describe('manageMembership.slice — simple reducers', () => {
  it('setHasChanges', () => {
    expect(manageMembershipReducer(initial, setHasChanges(true)).hasChanges).toBe(true);
  });

  it('setCurrentStep', () => {
    expect(manageMembershipReducer(initial, setCurrentStep(2)).currentStep).toBe(2);
  });

  it('setSelectedDestination sets destination', () => {
    const dest = { id: 'g1', name: 'Group1', type: 'GroupMembership' as any };
    const state = manageMembershipReducer(initial, setSelectedDestination(dest));
    expect(state.selectedDestination?.id).toBe('g1');
  });

  it('setSelectedDestination clears onboardingStatus when id is undefined', () => {
    const seeded = { ...initial, onboardingStatus: { status: OnboardingStatus.ReadyForOnboarding } };
    const state = manageMembershipReducer(seeded, setSelectedDestination(undefined));
    expect(state.onboardingStatus).toBeNull();
  });

  it('setNewJobQuery', () => {
    const query = [{ type: 'GroupMembership', source: 'src' }];
    expect(manageMembershipReducer(initial, setNewJobQuery(query as any)).newJob.query).toEqual(query);
  });

  it('setNewJobRequestor', () => {
    expect(manageMembershipReducer(initial, setNewJobRequestor('user@ex.com')).newJob.requestor).toBe('user@ex.com');
  });

  it('setNewJobStartDate', () => {
    const date = '2026-01-01T00:00:00Z';
    expect(manageMembershipReducer(initial, setNewJobStartDate(date)).newJob.startDate).toBe(date);
  });

  it('setNewJobThresholdPercentageForAdditions', () => {
    expect(manageMembershipReducer(initial, setNewJobThresholdPercentageForAdditions(50)).newJob.thresholdPercentageForAdditions).toBe(50);
  });

  it('setNewJobThresholdPercentageForRemovals', () => {
    expect(manageMembershipReducer(initial, setNewJobThresholdPercentageForRemovals(30)).newJob.thresholdPercentageForRemovals).toBe(30);
  });

  it('setUseThresholdLimits', () => {
    expect(manageMembershipReducer(initial, setUseThresholdLimits('No')).useThresholdLimits).toBe('No');
  });

  it('setStartDateOption', () => {
    expect(manageMembershipReducer(initial, setStartDateOption('RequestedDate')).startDateOption).toBe('RequestedDate');
  });

  it('setShowIncreaseDropdown', () => {
    expect(manageMembershipReducer(initial, setShowIncreaseDropdown(false)).showIncreaseDropdown).toBe(false);
  });

  it('setShowDecreaseDropdown', () => {
    expect(manageMembershipReducer(initial, setShowDecreaseDropdown(false)).showDecreaseDropdown).toBe(false);
  });

  it('resetManageMembership returns initial state', () => {
    const modified = manageMembershipReducer(initial, setCurrentStep(5));
    const state = manageMembershipReducer(modified, resetManageMembership());
    expect(state.currentStep).toBe(0);
    expect(state.hasChanges).toBe(false);
  });

  it('setIsMissingAndOrOperator', () => {
    expect(manageMembershipReducer(initial, setIsMissingAndOrOperator(true)).isMissingAndOrOperator).toBe(true);
  });

  it('setIsAdvancedQueryValid', () => {
    expect(manageMembershipReducer(initial, setIsAdvancedQueryValid(true)).isAdvancedQueryValid).toBe(true);
  });

  it('setCreatedGroupName', () => {
    expect(manageMembershipReducer(initial, setCreatedGroupName('New Group')).createdGroupName).toBe('New Group');
  });

  it('setCreateGroupErrorMessage', () => {
    expect(manageMembershipReducer(initial, setCreateGroupErrorMessage('Error!')).createGroupErrorMessage).toBe('Error!');
  });

  it('setBusinessJustification', () => {
    expect(manageMembershipReducer(initial, setBusinessJustification('Reason')).businessJustification).toBe('Reason');
  });

  it('setGroupSettings', () => {
    const settings = { allowEmailBasedSubscriptions: true } as any;
    expect(manageMembershipReducer(initial, setGroupSettings(settings)).groupSettings).toEqual(settings);
  });

  it('clearGroupMembers', () => {
    const seeded = { ...initial, groupMembers: { groupId: 'g', groupMemberCount: 1, groups: [] } };
    expect(manageMembershipReducer(seeded, clearGroupMembers()).groupMembers).toBeUndefined();
  });
});

describe('manageMembership.slice — source part reducers', () => {
  it('setSourceParts sets the array', () => {
    const parts = [makeSourcePart('p1')];
    expect(manageMembershipReducer(initial, setSourceParts(parts)).sourceParts).toHaveLength(1);
  });

  it('addSourcePart appends a part', () => {
    const seeded = { ...initial, sourceParts: [makeSourcePart('p1')] };
    const state = manageMembershipReducer(seeded, addSourcePart(makeSourcePart('p2')));
    expect(state.sourceParts).toHaveLength(2);
  });

  it('copySourcePart appends a copy', () => {
    const seeded = { ...initial, sourceParts: [makeSourcePart('p1')] };
    const state = manageMembershipReducer(seeded, copySourcePart(makeSourcePart('p1-copy')));
    expect(state.sourceParts).toHaveLength(2);
  });

  it('deleteSourcePart removes by id', () => {
    const seeded = { ...initial, sourceParts: [makeSourcePart('p1'), makeSourcePart('p2')] };
    const state = manageMembershipReducer(seeded, deleteSourcePart('p1'));
    expect(state.sourceParts).toHaveLength(1);
    expect(state.sourceParts[0].id).toBe('p2');
  });

  it('clearSourceParts empties the array', () => {
    const seeded = { ...initial, sourceParts: [makeSourcePart('p1')] };
    expect(manageMembershipReducer(seeded, clearSourceParts()).sourceParts).toHaveLength(0);
  });

  it('updateSourcePartType switches to HR', () => {
    const seeded = { ...initial, sourceParts: [makeSourcePart('p1', SourcePartType.GroupMembership)] };
    const state = manageMembershipReducer(seeded, updateSourcePartType({ partId: 'p1', type: SourcePartType.HR }));
    expect(state.sourceParts[0].query.type).toBe(SourcePartType.HR);
  });

  it('updateSourcePartType switches to GroupOwnership', () => {
    const seeded = { ...initial, sourceParts: [makeSourcePart('p1')] };
    const state = manageMembershipReducer(seeded, updateSourcePartType({ partId: 'p1', type: SourcePartType.GroupOwnership }));
    expect(state.sourceParts[0].query.type).toBe(SourcePartType.GroupOwnership);
  });

  it('updateSourcePartType switches to PlaceMembership', () => {
    const seeded = { ...initial, sourceParts: [makeSourcePart('p1')] };
    const state = manageMembershipReducer(seeded, updateSourcePartType({ partId: 'p1', type: SourcePartType.PlaceMembership }));
    expect(state.sourceParts[0].query.type).toBe(SourcePartType.PlaceMembership);
  });

  it('updateSourcePartType clears the stale title on type change', () => {
    const seeded = { ...initial, sourceParts: [makeSourcePart('p1', SourcePartType.GroupMembership, 'All Users in TestGroup')] };
    const state = manageMembershipReducer(seeded, updateSourcePartType({ partId: 'p1', type: SourcePartType.HR }));
    expect(state.sourceParts[0].title).toBe('');
  });

  it('updateSourcePart updates an existing part', () => {
    const seeded = { ...initial, sourceParts: [makeSourcePart('p1')] };
    const updated = { ...makeSourcePart('p1'), title: 'Updated Title' };
    const state = manageMembershipReducer(seeded, updateSourcePart(updated));
    expect(state.sourceParts[0].title).toBe('Updated Title');
  });
});

describe('manageMembership.slice — setIsAdvancedView', () => {
  it('switching to advanced view with source parts sets advancedViewQuery', () => {
    const parts = [makeSourcePart('p1')];
    const seeded = { ...initial, sourceParts: parts };
    const state = manageMembershipReducer(seeded, setIsAdvancedView(true));
    expect(state.isAdvancedView).toBe(true);
    expect(state.advancedViewQuery).toBeDefined();
    expect(state.advancedViewQuery).not.toBe('');
  });

  it('switching to advanced view with no source parts sets empty query', () => {
    const state = manageMembershipReducer(initial, setIsAdvancedView(true));
    expect(state.advancedViewQuery).toBe('');
  });

  it('switching from advanced view with valid query parses it back', () => {
    const query = JSON.stringify([{ type: 'GroupMembership', source: 'g1' }]);
    const seeded = {
      ...initial,
      isAdvancedView: true,
      advancedViewQuery: query,
      isAdvancedQueryValid: true,
      sourceParts: [makeSourcePart('p1')],
    };
    const state = manageMembershipReducer(seeded, setIsAdvancedView(false));
    expect(state.isAdvancedView).toBe(false);
    expect(state.sourceParts[0].query.type).toBe('GroupMembership');
  });
});

describe('manageMembership.slice — applyAdvancedViewQuery', () => {
  it('parses valid JSON array and updates source parts', () => {
    const query = JSON.stringify([{ type: 'GroupMembership', source: 'g1' }]);
    const state = manageMembershipReducer(initial, applyAdvancedViewQuery(query));
    expect(state.sourceParts).toHaveLength(1);
    expect(state.newJob.query).toHaveLength(1);
  });

  it('handles null payload', () => {
    const state = manageMembershipReducer(initial, applyAdvancedViewQuery(null as any));
    expect(state.advancedViewQuery).toBe('');
  });

  it('ignores non-array JSON', () => {
    const state = manageMembershipReducer(initial, applyAdvancedViewQuery('{"type":"x"}'));
    expect(state.sourceParts).toHaveLength(0);
  });

  it('ignores invalid JSON', () => {
    const state = manageMembershipReducer(initial, applyAdvancedViewQuery('not-json'));
    expect(state.sourceParts).toHaveLength(0);
  });
});

describe('manageMembership.slice — setIsEditingExistingJob', () => {
  it('resets state when set to false', () => {
    const modified = manageMembershipReducer(initial, setCurrentStep(3));
    const state = manageMembershipReducer(modified, setIsEditingExistingJob(false));
    expect(state.currentStep).toBe(0);
    expect(state.isEditingExistingJob).toBe(false);
  });

  it('sets flag to true without resetting', () => {
    const modified = manageMembershipReducer(initial, setCurrentStep(3));
    const state = manageMembershipReducer(modified, setIsEditingExistingJob(true));
    expect(state.isEditingExistingJob).toBe(true);
    expect(state.currentStep).toBe(3);
  });
});

describe('manageMembership.slice — extraReducers', () => {
  it('searchDestinations.pending sets loading', () => {
    const state = manageMembershipReducer(initial, searchDestinations.pending('req1', '' as any));
    expect(state.loadingSearchResults).toBe(true);
  });

  it('searchDestinations.fulfilled clears loading and sets results', () => {
    const results = [{ key: 1, text: 'G1' }] as any;
    const state = manageMembershipReducer(initial, searchDestinations.fulfilled(results, 'req1', '' as any));
    expect(state.loadingSearchResults).toBe(false);
    expect(state.searchResults).toEqual(results);
  });

  it('searchDestinations.rejected clears loading', () => {
    const state = manageMembershipReducer(initial, searchDestinations.rejected(new Error('e'), 'req1', '' as any));
    expect(state.loadingSearchResults).toBe(false);
  });

  it('searchChannels.pending sets loading', () => {
    const state = manageMembershipReducer(initial, searchChannels.pending('req1', '' as any));
    expect(state.loadingSearchResults).toBe(true);
  });

  it('searchChannels.fulfilled sets channel results', () => {
    const results = [{ key: 1, text: 'Ch1' }] as any;
    const state = manageMembershipReducer(initial, searchChannels.fulfilled(results, 'req1', '' as any));
    expect(state.channelPickerSearchResults).toEqual(results);
  });

  it('getGroupOnboardingStatus.fulfilled sets status', () => {
    const status = { status: OnboardingStatus.ReadyForOnboarding };
    const state = manageMembershipReducer(initial, getGroupOnboardingStatus.fulfilled(status, 'req1', '' as any));
    expect(state.onboardingStatus).toEqual(status);
  });

  it('getGroupOnboardingStatus.pending clears status', () => {
    const seeded = { ...initial, onboardingStatus: { status: OnboardingStatus.ReadyForOnboarding } };
    const state = manageMembershipReducer(seeded, getGroupOnboardingStatus.pending('req1', '' as any));
    expect(state.onboardingStatus).toBeNull();
  });

  it('getChannelOnboardingStatus.fulfilled sets status', () => {
    const status = { status: OnboardingStatus.ReadyForOnboarding };
    const state = manageMembershipReducer(
      initial,
      getChannelOnboardingStatus.fulfilled(status, 'req1', { teamId: 't', channelId: 'c' } as any)
    );
    expect(state.onboardingStatus).toEqual(status);
  });

  it('getGroupEndpoints.fulfilled sets endpoints on matching destination', () => {
    const seeded = { ...initial, selectedDestination: { id: 'g1', name: 'G1', type: 'GroupMembership' as any } };
    const endpoints = ['ep1', 'ep2'];
    const state = manageMembershipReducer(
      seeded,
      getGroupEndpoints.fulfilled(endpoints, 'req1', 'g1')
    );
    expect(state.selectedDestination?.endpoints).toEqual(endpoints);
  });

  it('getGroupOwners.fulfilled sets owners', () => {
    const owners = [{ objectId: 'o1', displayName: 'Owner 1' }] as any;
    const state = manageMembershipReducer(initial, getGroupOwners.fulfilled(owners, 'req1', '' as any));
    expect(state.groupOwners).toEqual(owners);
  });

  it('getGroupMembers.pending clears members', () => {
    const seeded = { ...initial, groupMembers: { groupId: 'g', groupMemberCount: 1, groups: [] } };
    const state = manageMembershipReducer(seeded, getGroupMembers.pending('req1', '' as any));
    expect(state.groupMembers).toBeUndefined();
  });

  it('getGroupMembers.fulfilled sets members', () => {
    const members = { groupId: 'g1', groupMemberCount: 5, groups: [] };
    const state = manageMembershipReducer(initial, getGroupMembers.fulfilled(members as any, 'req1', '' as any));
    expect(state.groupMembers).toEqual(members);
  });

  it('createGroup.pending sets loading', () => {
    const state = manageMembershipReducer(initial, createGroup.pending('req1', {} as any));
    expect(state.createGroupLoading).toBe(true);
  });

  it('createGroup.fulfilled with groupId sets destination', () => {
    const seeded = { ...initial, createdGroupName: 'New Group' };
    const payload = { groupId: 'g-new' };
    const state = manageMembershipReducer(seeded, createGroup.fulfilled(payload as any, 'req1', {} as any));
    expect(state.createGroupLoading).toBe(false);
    expect(state.createdGroupId).toBe('g-new');
    expect(state.selectedDestination?.name).toBe('New Group');
    expect(state.onboardingStatus?.status).toBe(OnboardingStatus.ReadyForOnboarding);
  });

  it('createGroup.rejected sets error', () => {
    const state = manageMembershipReducer(initial, createGroup.rejected(new Error('create fail'), 'req1', {} as any));
    expect(state.createGroupLoading).toBe(false);
    expect(state.createGroupErrorMessage).toBe('create fail');
  });
});

describe('manageMembership.slice — selectors', () => {
  it('manageMembershipIsGroupReadyForOnboarding returns true', () => {
    const root = buildRoot({ onboardingStatus: { status: OnboardingStatus.ReadyForOnboarding } });
    expect(manageMembershipIsGroupReadyForOnboarding(root)).toBe(true);
  });

  it('manageMembershipIsGroupReadyForOnboarding returns false for null', () => {
    expect(manageMembershipIsGroupReadyForOnboarding(buildRoot())).toBe(false);
  });

  it('areAllSourcePartsValid returns false for empty parts', () => {
    expect(areAllSourcePartsValid(buildRoot())).toBe(false);
  });

  it('manageMembershipIsToggleEnabled in regular view with valid parts', () => {
    const parts = [makeSourcePart('p1')];
    expect(manageMembershipIsToggleEnabled(buildRoot({ sourceParts: parts }))).toBe(true);
  });

  it('manageMembershipIsToggleEnabled in advanced view with valid query', () => {
    const root = buildRoot({ isAdvancedView: true, isAdvancedQueryValid: true });
    expect(manageMembershipIsToggleEnabled(root)).toBe(true);
  });

  it('manageMembershipIsToggleEnabled in advanced view with empty query', () => {
    const root = buildRoot({ isAdvancedView: true, isAdvancedQueryValid: false, advancedViewQuery: '' });
    expect(manageMembershipIsToggleEnabled(root)).toBe(true);
  });

  it('manageMembershipIsToggleEnabled in advanced view with [] query', () => {
    const root = buildRoot({ isAdvancedView: true, isAdvancedQueryValid: false, advancedViewQuery: '[]' });
    expect(manageMembershipIsToggleEnabled(root)).toBe(true);
  });
});

describe('buildCompositeQuery', () => {
  it('maps source parts to their queries', () => {
    const parts = [makeSourcePart('p1'), makeSourcePart('p2')];
    const result = buildCompositeQuery(parts);
    expect(result).toHaveLength(2);
  });

  it('handles part with undefined query', () => {
    const part = { ...makeSourcePart('p1'), query: undefined } as any;
    const result = buildCompositeQuery([part]);
    expect(result[0]).toBeUndefined();
  });
});
