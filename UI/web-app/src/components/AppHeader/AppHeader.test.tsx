import React from 'react';
import { render } from '@testing-library/react';
import '@testing-library/jest-dom';
import { AppHeader } from './AppHeader';

describe('AppHeader', () => {
  it('renders', () => {
    const { asFragment } = render(<AppHeader />);
    expect(asFragment()).toMatchSnapshot();
  });
});
