import React from 'react';
import { render, screen } from '@testing-library/react';
import WelcomeName from './WelcomeName';

test('renders the name', () => {
  render(<WelcomeName />);
  const welcome = screen.getByText(/welcome/i);
  expect(welcome).toBeInTheDocument();
});