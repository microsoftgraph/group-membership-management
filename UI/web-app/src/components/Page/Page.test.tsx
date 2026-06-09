// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { screen } from '@testing-library/react';

import { renderWithProviders } from '../../testing/renderWithProviders';
import { Page } from './Page';

describe('Page', () => {
  it('renders provided children', () => {
    renderWithProviders(
      <Page>
        <span>Page Content</span>
      </Page>
    );

    expect(screen.getByText('Page Content')).toBeInTheDocument();
  });

  it('applies custom class names to the root element', () => {
    renderWithProviders(
      <Page className="custom-class">
        <span>Styled Content</span>
      </Page>
    );

    expect(screen.getByText('Styled Content').parentElement).toHaveClass('custom-class');
  });
});
