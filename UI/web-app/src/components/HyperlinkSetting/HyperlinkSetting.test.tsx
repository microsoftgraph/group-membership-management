import React from 'react';
import { fireEvent, screen, waitFor } from '@testing-library/react';
import { HyperlinkSetting } from './HyperlinkSetting';
import { renderWithProviders } from '../../testing/renderWithProviders';

describe('HyperlinkSetting', () => {
  const defaultProps = {
    title: 'Dashboard link',
    description: 'Configure the dashboard hyperlink.',
    link: '',
    required: false,
    onLinkChange: jest.fn(),
    onValidation: jest.fn(),
  };

  beforeEach(() => {
    defaultProps.onLinkChange.mockClear();
    defaultProps.onValidation.mockClear();
  });

  it('calls onLinkChange as the user types', () => {
    renderWithProviders(<HyperlinkSetting {...defaultProps} />);

    fireEvent.change(screen.getByLabelText('Address'), {
      target: { value: 'https://contoso.com' },
    });

    expect(defaultProps.onLinkChange).toHaveBeenCalledWith('https://contoso.com');
  });

  it('treats an optional empty value as valid', async () => {
    renderWithProviders(<HyperlinkSetting {...defaultProps} />);

    const input = screen.getByLabelText('Address');
    fireEvent.blur(input);

    await waitFor(() =>
      expect(defaultProps.onValidation).toHaveBeenCalledWith(true)
    );
  });

  it('shows an error when the URL cannot be parsed', async () => {
    renderWithProviders(
      <HyperlinkSetting
        {...defaultProps}
        link="invalid"
        required
      />
    );

    const input = screen.getByLabelText('Address');
    fireEvent.blur(input);

    await waitFor(() =>
      expect(defaultProps.onValidation).toHaveBeenCalledWith(false)
    );
    expect(await screen.findByText('Invalid URL')).toBeInTheDocument();
  });
});
