// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { describe, expect, it } from 'vitest';
import { act, screen } from '@testing-library/react';
import type { AnyAction } from '@reduxjs/toolkit';

import { renderWithProviders } from '../../testing/renderWithProviders';
import { defaultStrings } from '../../services/localization';
import { UserSpotCheckBase } from './UserSpotCheck.base';
import { ISourcePart } from '../../models/ISourcePart';
import { SpotCheckResult } from '../../models/SpotCheckResult';
import { spotCheckUser } from '../../store/spotCheck.api';

// Note: @fluentui/react <MessageBar> renders its string children empty under
// jsdom (it relies on layout measurement that jsdom does not implement). It
// renders correctly in the browser. Tests therefore assert MessageBar presence
// via its CSS class and assert text content on the plain spans that do render.
//
// The component clears any spot-check state on mount (see the clearSpotCheck
// effect in UserSpotCheck.base.tsx), so preloaded state would be wiped before
// assertions run. Tests instead render first, then dispatch the spotCheckUser
// thunk's fulfilled/rejected action to populate state after mount.

const spotCheckStrings = defaultStrings.Components.UserSpotCheck;

const sourceParts = [
  { id: '1', title: 'Group part', query: {}, isNew: false, isExpanded: true },
  { id: '2', title: 'HR part', query: {}, isNew: false, isExpanded: true },
  { id: '3', title: 'Teams part', query: {}, isNew: false, isExpanded: true },
] as unknown as ISourcePart[];

const thunkArg = { syncJobId: 'job-1', userId: 'user-1' };

const renderComponent = (action?: AnyAction) => {
  const utils = renderWithProviders(<UserSpotCheckBase syncJobId="job-1" sourceParts={sourceParts} />, {
    preloadedState: {
      localization: { language: 'en', strings: defaultStrings },
    } as never,
  });
  if (action) {
    act(() => {
      utils.store.dispatch(action);
    });
  }
  return utils;
};

const succeededWith = (result: SpotCheckResult): AnyAction =>
  spotCheckUser.fulfilled(result, 'request-id', thunkArg);

describe('UserSpotCheckBase', () => {
  it('renders the title, description and search label', () => {
    renderComponent();
    expect(screen.getByText(spotCheckStrings.title)).toBeInTheDocument();
    expect(screen.getByText(spotCheckStrings.description)).toBeInTheDocument();
    expect(screen.getByText(spotCheckStrings.searchLabel)).toBeInTheDocument();
  });

  it('shows an error MessageBar and no part rows when the account is disabled', () => {
    const result: SpotCheckResult = { accountEnabled: false, hasUnsupportedParts: false, parts: [] };
    const { container } = renderComponent(succeededWith(result));

    expect(container.querySelector('.ms-MessageBar--error')).toBeInTheDocument();
    // No part titles should be rendered when the account is disabled.
    expect(screen.queryByText('Group part')).not.toBeInTheDocument();
  });

  it('renders per-part statuses, exclusionary state and the unsupported banner', () => {
    const result: SpotCheckResult = {
      accountEnabled: true,
      hasUnsupportedParts: true,
      parts: [
        { index: 0, type: 'GroupMembership', supported: true, exclusionary: false, included: true },
        { index: 1, type: 'SqlMembership', supported: true, exclusionary: true, included: false },
        { index: 2, type: 'TeamsChannelMembership', supported: false, exclusionary: false, included: null },
      ],
    };
    const { container } = renderComponent(succeededWith(result));

    // Part titles resolved from sourceParts by index.
    expect(screen.getByText('Group part')).toBeInTheDocument();
    expect(screen.getByText('HR part')).toBeInTheDocument();
    expect(screen.getByText('Teams part')).toBeInTheDocument();

    // Status labels (rendered in plain spans).
    expect(screen.getByText(spotCheckStrings.included)).toBeInTheDocument();
    expect(screen.getByText(spotCheckStrings.exclusionaryNotIncluded)).toBeInTheDocument();
    expect(screen.getByText(spotCheckStrings.notSupported)).toBeInTheDocument();

    // Unsupported parts warning banner is present.
    expect(container.querySelector('.ms-MessageBar--warning')).toBeInTheDocument();
  });

  it('does not render the unsupported banner when there are no unsupported parts', () => {
    const result: SpotCheckResult = {
      accountEnabled: true,
      hasUnsupportedParts: false,
      parts: [
        { index: 0, type: 'GroupMembership', supported: true, exclusionary: false, included: false },
      ],
    };
    const { container } = renderComponent(succeededWith(result));

    expect(screen.getByText(spotCheckStrings.notIncluded)).toBeInTheDocument();
    expect(container.querySelector('.ms-MessageBar--warning')).not.toBeInTheDocument();
  });

  it('shows an error MessageBar when the request failed', () => {
    const { container } = renderComponent(
      spotCheckUser.rejected(new Error('notFound'), 'request-id', thunkArg)
    );
    expect(container.querySelector('.ms-MessageBar--error')).toBeInTheDocument();
  });
});
