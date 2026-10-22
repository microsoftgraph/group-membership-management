// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { RootState } from '../../store';
import type { ISourcePart } from '../../models/ISourcePart';
import { SourcePartType } from '../../models/SourcePartType';
import { OfflineAuthenticationService } from '../../testing/OfflineAuthenticationService';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { GroupQuerySourceBase } from './GroupQuerySource.base';

vi.mock('@fluentui/react', async () => {
  const actual = await vi.importActual<typeof import('@fluentui/react')>('@fluentui/react');
  return {
    ...actual,
    NormalPeoplePicker: (props: {
      onChange?: (items?: Array<{ id?: string; text?: string }>) => void;
      selectedItems?: Array<{ text?: string }>;
    }) => (
      <button
        type="button"
        aria-label="Replace group"
        onClick={() => props.onChange?.([{ id: 'replacement-group', text: 'Replacement group' }])}
      >
        {props.selectedItems?.[0]?.text}
      </button>
    ),
  };
});

const part: ISourcePart = {
  id: 'group-part',
  title: 'All users in original group',
  query: {
    type: SourcePartType.GroupMembership,
    source: 'original-group',
    exclusionary: false,
    identityTypes: ['agents'],
  },
  isNew: false,
  isExpanded: true,
};

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('GroupQuerySource', () => {
  it('repicks and retitles a selected group without exposing an identity-type control', () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(() => new Promise<Response>(() => {}))
    );
    const onSourceChange = vi.fn();
    const preloadedState: Partial<RootState> = {
      groupPart: {
        searchResults: [{
          key: 0,
          id: 'original-group',
          text: 'Original group',
          secondaryText: 'original@example.test',
        }],
        resolvedGroupsById: {
          'original-group': {
            key: 0,
            id: 'original-group',
            text: 'Original group',
            secondaryText: 'original@example.test',
          },
        },
      },
    };

    renderWithProviders(
      <GroupQuerySourceBase
        part={part}
        onSourceChange={onSourceChange}
      />,
      {
        preloadedState,
        serviceMocks: {
          authenticationService: new OfflineAuthenticationService(),
        },
      }
    );

    expect(screen.getByRole('button', { name: 'Replace group' })).toHaveTextContent('Original group');
    expect(screen.queryByText(/identity types?/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('checkbox', { name: /agents?|people|users/i })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Replace group' }));

    expect(onSourceChange).toHaveBeenCalledTimes(1);
    expect(onSourceChange.mock.calls[0][0]).toBe('replacement-group');
    expect(onSourceChange.mock.calls[0][1]).toContain('Replacement group');
  });
});
