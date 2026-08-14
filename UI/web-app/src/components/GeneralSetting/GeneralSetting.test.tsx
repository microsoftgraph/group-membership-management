import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { GeneralSetting } from './GeneralSetting';

describe('GeneralSetting', () => {
  const baseProps = {
    id: 'general-setting-toggle',
    title: 'Allow self-service sync',
    description: 'Owners can configure syncs without admin assistance.',
    generalSettingValue: 'true',
    onGeneralSettingChange: jest.fn(),
  };

  beforeEach(() => {
    baseProps.onGeneralSettingChange.mockClear();
  });

  it('renders the provided title and description', () => {
    render(<GeneralSetting {...baseProps} />);

    expect(screen.getByText(baseProps.title)).toBeInTheDocument();
    expect(screen.getByText(baseProps.description)).toBeInTheDocument();
  });

  it('initializes the toggle based on the current value', () => {
    render(<GeneralSetting {...baseProps} />);

    expect(screen.getByRole('switch')).toBeChecked();
  });

  it('notifies the caller when clicked', () => {
    render(<GeneralSetting {...baseProps} />);

    fireEvent.click(screen.getByRole('switch'));

    expect(baseProps.onGeneralSettingChange).toHaveBeenCalledWith('false');
  });

  it('allows toggling from a disabled initial state', () => {
    render(<GeneralSetting {...baseProps} generalSettingValue="false" />);

    const toggle = screen.getByRole('switch');
    expect(toggle).not.toBeChecked();

    fireEvent.click(toggle);

    expect(baseProps.onGeneralSettingChange).toHaveBeenCalledWith('true');
  });

  it('reflects the value when it arrives after the initial render', () => {
    const { rerender } = render(<GeneralSetting {...baseProps} generalSettingValue="false" />);

    expect(screen.getByRole('switch')).not.toBeChecked();

    rerender(<GeneralSetting {...baseProps} generalSettingValue="true" />);

    expect(screen.getByRole('switch')).toBeChecked();
  });
});
