import React from 'react';
import { screen } from '@testing-library/react';
import { InfoLabel } from './InfoLabel';
import { renderWithProviders } from '../testing/renderWithProviders';

describe('InfoLabel', () => {
  it('renders the label and info icon with accessible tooltip trigger', () => {
    const description = 'List of group members';
    const { container } = renderWithProviders(
      <InfoLabel label="Members" description={description} />
    );

    expect(screen.getByText('Members')).toBeInTheDocument();
    expect(container.querySelector('[data-icon-name="Info"]')).not.toBeNull();
    expect(screen.getByText(description)).toBeInTheDocument();
    expect(container.querySelector('.ms-TooltipHost')).not.toBeNull();
  });
});
