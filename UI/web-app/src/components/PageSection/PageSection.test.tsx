// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { screen } from '@testing-library/react';

import { renderWithProviders } from '../../testing/renderWithProviders';
import { PageSection } from './PageSection';

describe('PageSection', () => {
  it('renders the expected children', () => {
    renderWithProviders(
      <PageSection>
        <div>Section Body</div>
      </PageSection>
    );

    expect(screen.getByText('Section Body')).toBeInTheDocument();
  });

  it('honors provided class names', () => {
    renderWithProviders(
      <PageSection className="section-class">
        <div>Styled Section</div>
      </PageSection>
    );

    expect(screen.getByText('Styled Section').parentElement).toHaveClass('section-class');
  });
});
