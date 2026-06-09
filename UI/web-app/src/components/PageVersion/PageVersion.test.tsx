import React from 'react';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../testing/renderWithProviders';
import { PageVersion } from './PageVersion';

describe('PageVersion', () => {
  const originalVersion = process.env.REACT_APP_VERSION_NUMBER;

  afterEach(() => {
    if (originalVersion === undefined) {
      delete process.env.REACT_APP_VERSION_NUMBER;
    } else {
      process.env.REACT_APP_VERSION_NUMBER = originalVersion;
    }
  });

  it('displays the configured version string', () => {
    process.env.REACT_APP_VERSION_NUMBER = '9.9.9';

    renderWithProviders(<PageVersion />);

    expect(screen.getByText('Version #9.9.9')).toBeInTheDocument();
  });

  it('falls back to the default version when not configured', () => {
    process.env.REACT_APP_VERSION_NUMBER = '';

    renderWithProviders(<PageVersion />);

    expect(screen.getByText('Version #1.0.0')).toBeInTheDocument();
  });
});
