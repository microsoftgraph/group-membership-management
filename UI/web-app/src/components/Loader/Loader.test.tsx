// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { screen } from '@testing-library/react';

import { renderWithProviders } from '../../testing/renderWithProviders';
import { Loader } from './Loader';

describe('Loader', () => {
  it('shows a progress spinner with localized text', () => {
    const { container } = renderWithProviders(<Loader />);

    expect(container.querySelector('.ms-Spinner')).not.toBeNull();
    expect(screen.getByText(/Loading/)).toHaveTextContent('Loading...');
  });

  it('merges custom class names onto the root element', () => {
    const { container } = renderWithProviders(
      <Loader className="loader-class" />
    );

    const rootElement = container.firstElementChild as HTMLElement;
    expect(rootElement).toHaveClass('loader-class');
  });
});
