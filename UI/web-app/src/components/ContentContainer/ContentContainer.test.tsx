import React from 'react';
import { fireEvent, screen } from '@testing-library/react';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { ContentContainer } from './ContentContainer';

describe('ContentContainer', () => {
  it('renders title, children, and action buttons', () => {
    const onClick = jest.fn();

    renderWithProviders(
      <ContentContainer
        title="Container Title"
        actionButtons={[{ text: 'Refresh', onClick, icon: { iconName: 'Refresh' } }]}
      >
        <div>Child content</div>
      </ContentContainer>
    );

    expect(screen.getByText('Container Title')).toBeInTheDocument();
    expect(screen.getByText('Child content')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it('hides the separator when requested', () => {
    const { container } = renderWithProviders(
      <ContentContainer title="No Separator" hideSeparator>
        <div>Content</div>
      </ContentContainer>
    );

    expect(container.querySelector('[role="separator"]')).toBeNull();
  });
});
