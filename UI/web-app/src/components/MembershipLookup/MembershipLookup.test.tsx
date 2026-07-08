// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';

import { renderWithProviders } from '../../testing/renderWithProviders';
import { defaultStrings } from '../../services/localization';
import { MembershipLookupBase, evaluateLookup } from './MembershipLookup.base';
import {
  MembershipChangeType,
  type SearchSyncHistoryByUserResult,
} from '../../models/SearchSyncHistoryByUserResult';

const lookupStrings = defaultStrings.Components.MembershipLookup;
const RUN_ID = 'AAAA1111-BBBB-2222-CCCC-333333333333';

const baseResult = (overrides: Partial<SearchSyncHistoryByUserResult> = {}): SearchSyncHistoryByUserResult => ({
  matchingRunIds: [],
  runMembershipChanges: [],
  userInCurrentGroup: false,
  checkedCurrentGroupMembership: true,
  ...overrides,
});

describe('evaluateLookup', () => {
  it('reports the user as a current member when checked and in the group', () => {
    const { isMember } = evaluateLookup(baseResult({ userInCurrentGroup: true }), RUN_ID);
    expect(isMember).toBe(true);
  });

  it('reports not a member when the group membership was not checked', () => {
    const { isMember } = evaluateLookup(
      baseResult({ checkedCurrentGroupMembership: false, userInCurrentGroup: true }),
      RUN_ID
    );
    expect(isMember).toBe(false);
  });

  it('maps an Added change for this run to "add" (case-insensitive run id match)', () => {
    const { pendingAction } = evaluateLookup(
      baseResult({
        runMembershipChanges: [
          { runId: RUN_ID.toLowerCase(), membershipChangeType: MembershipChangeType.Added },
        ],
      }),
      RUN_ID
    );
    expect(pendingAction).toBe('add');
  });

  it('maps a Removed change for this run to "remove"', () => {
    const { pendingAction } = evaluateLookup(
      baseResult({
        runMembershipChanges: [
          { runId: RUN_ID, membershipChangeType: MembershipChangeType.Removed },
        ],
      }),
      RUN_ID
    );
    expect(pendingAction).toBe('remove');
  });

  it('returns "none" when no change matches this run id', () => {
    const { pendingAction } = evaluateLookup(
      baseResult({
        runMembershipChanges: [
          { runId: 'some-other-run', membershipChangeType: MembershipChangeType.Added },
        ],
      }),
      RUN_ID
    );
    expect(pendingAction).toBe('none');
  });
});

describe('MembershipLookupBase', () => {
  it('renders the title and description in the idle state and exposes the search picker', () => {
    renderWithProviders(<MembershipLookupBase syncJobId="job-1" runId={RUN_ID} />, {
      preloadedState: {
        localization: { language: 'en', strings: defaultStrings },
      } as never,
    });
    expect(screen.getByText(lookupStrings.title)).toBeInTheDocument();
    expect(screen.getByText(lookupStrings.description)).toBeInTheDocument();
    expect(screen.getByLabelText(lookupStrings.searchLabel)).toBeInTheDocument();
  });
});
