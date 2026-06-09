import React from 'react';
import { screen } from '@testing-library/react';
import { InfoWord } from './InfoWord';
import { renderWithProviders } from '../testing/renderWithProviders';

describe('InfoWord', () => {
  it('renders the label next to the info icon with accessible tooltip trigger', () => {
    const description = 'Explains the selected option';
    const { container } = renderWithProviders(
      <InfoWord label="Details" description={description} />
    );

    expect(screen.getByText('Details')).toBeInTheDocument();
    expect(container.querySelector('[data-icon-name="Info"]')).not.toBeNull();
    expect(screen.getByText(description)).toBeInTheDocument();
    expect(container.querySelector('.ms-TooltipHost')).not.toBeNull();
  });
});
