// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import type { ISourcePart } from '../models/ISourcePart';
import type { GroupMembershipSourcePart } from '../models/GroupMembershipSourcePart';
import { SourcePartType } from '../models/SourcePartType';
import { renderWithProviders } from '../testing/renderWithProviders';
import { useTitleProcessing } from './useTitleProcessing';

const TitleProcessingHarness = (props: Parameters<typeof useTitleProcessing>[0]) => {
  useTitleProcessing(props);
  return null;
};

describe('useTitleProcessing', () => {
  it('preserves an agents-only group selection exactly when rebuilding a generated title', () => {
    const agentsOnlyPart: ISourcePart = {
      id: 'group-part',
      title: '',
      query: {
        type: SourcePartType.GroupMembership,
        source: 'group-guid',
        exclusionary: false,
        identityTypes: ['agents'],
      },
      isNew: false,
      isExpanded: false,
    };

    const { store } = renderWithProviders(
      <TitleProcessingHarness
        generatedTitlesYet
        jobWithNoTitles
        sourceParts={[agentsOnlyPart]}
        titles={[]}
        generatedHRParts={[]}
        generatedGroupParts={[{ id: 'group-part', title: 'Generated agents-only group title' }]}
        withSummarizedCriteriaString="with {0}"
      />
    );

    const rebuiltPart = store.getState().manageMembership.sourceParts[0];
    expect(rebuiltPart.title).toBe('Generated agents-only group title');
    expect(rebuiltPart.query).toStrictEqual(agentsOnlyPart.query);
    expect((rebuiltPart.query as GroupMembershipSourcePart).identityTypes).toStrictEqual(['agents']);
  });
});
