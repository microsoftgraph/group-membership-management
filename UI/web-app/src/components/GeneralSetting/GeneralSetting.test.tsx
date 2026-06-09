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

  it('toggles the value and notifies the caller when clicked', () => {
    render(<GeneralSetting {...baseProps} />);

    const toggle = screen.getByRole('switch');
    fireEvent.click(toggle);

    expect(baseProps.onGeneralSettingChange).toHaveBeenCalledWith('false');
    expect(toggle).not.toBeChecked();
  });

  it('allows toggling from a disabled initial state', () => {
    render(<GeneralSetting {...baseProps} generalSettingValue="false" />);

    const toggle = screen.getByRole('switch');
    expect(toggle).not.toBeChecked();

    fireEvent.click(toggle);

    expect(baseProps.onGeneralSettingChange).toHaveBeenCalledWith('true');
    expect(toggle).toBeChecked();
  });
});
