// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ThresholdExceededActionDialogBase } from './ThresholdExceededActionDialog.base';
import type { IThresholdExceededActionDialogProps } from './ThresholdExceededActionDialog.types';

vi.mock('../../store/hooks', () => ({
  useStrings: () => ({
    cancel: 'Cancel',
    JobDetails: {
      Panel: {
        ThresholdExceededActionDialog: {
          title: 'Threshold Exceeded',
          warningText: 'Changes detected for {0}.',
          additionsDetailsText: '{0} users added ({1}%), threshold is {2}%.',
          removalsDetailsText: '{0} users removed ({1}%), threshold is {2}%.',
          purgeDateText: 'Changes will be purged on {0}.',
          applyChanges: 'Apply Changes',
          applyChangesDescription: 'Apply the pending changes.',
          editRules: 'Edit Rules',
          editRulesDescription: 'Edit the membership rules.',
          editThreshold: 'Edit Threshold',
          editThresholdDescription: 'Current thresholds: {0} for additions, {1} for removals.',
          pauseSync: 'Pause Sync',
          pauseSyncDescription: 'Pause synchronization.',
        },
      },
    },
  }),
}));

const defaultProps: IThresholdExceededActionDialogProps = {
  isOpen: true,
  isLoading: false,
  onDismiss: vi.fn(),
  groupName: 'Test Group',
  usersToAdd: 10,
  increasePercentage: 50,
  thresholdPercentageForAdditions: 30,
  usersToRemove: 5,
  decreasePercentage: 25,
  thresholdPercentageForRemovals: 20,
  onApplyChanges: vi.fn(),
  onEditRules: vi.fn(),
  onEditThreshold: vi.fn(),
  onPauseSync: vi.fn(),
  isApplyChangesEnabled: true,
  isEditRulesEnabled: true,
  isEditThresholdEnabled: true,
  isPauseSyncEnabled: true,
};

beforeEach(() => {
  vi.clearAllMocks();
});

describe('ThresholdExceededActionDialogBase', () => {
  it('renders the dialog when isOpen is true', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expect(screen.getByText('Threshold Exceeded')).toBeInTheDocument();
  });

  it('does not render content when isOpen is false', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} isOpen={false} />);
    expect(screen.queryByText('Threshold Exceeded')).not.toBeInTheDocument();
  });

  it('shows a spinner when isLoading is true', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} isLoading={true} />);
    expect(document.querySelector('.ms-Spinner')).toBeInTheDocument();
  });

  it('renders all four action cards', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expect(screen.getByText('Apply Changes')).toBeInTheDocument();
    expect(screen.getByText('Edit Rules')).toBeInTheDocument();
    expect(screen.getByText('Edit Threshold')).toBeInTheDocument();
    expect(screen.getByText('Pause Sync')).toBeInTheDocument();
  });

  it('calls onApplyChanges when the Apply Changes card is clicked', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    fireEvent.click(screen.getByText('Apply Changes'));
    expect(defaultProps.onApplyChanges).toHaveBeenCalledOnce();
  });

  it('calls onEditRules when the Edit Rules card is clicked', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    fireEvent.click(screen.getByText('Edit Rules'));
    expect(defaultProps.onEditRules).toHaveBeenCalledOnce();
  });

  it('calls onPauseSync when the Pause Sync card is clicked', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    fireEvent.click(screen.getByText('Pause Sync'));
    expect(defaultProps.onPauseSync).toHaveBeenCalledOnce();
  });

  it('does not call onClick when action is disabled', () => {
    render(
      <ThresholdExceededActionDialogBase
        {...defaultProps}
        isApplyChangesEnabled={false}
      />
    );
    fireEvent.click(screen.getByText('Apply Changes'));
    expect(defaultProps.onApplyChanges).not.toHaveBeenCalled();
  });

  it('renders error message bar when errorMessage is provided', () => {
    render(
      <ThresholdExceededActionDialogBase
        {...defaultProps}
        errorMessage="Something went wrong"
      />
    );
    const messageBar = document.querySelector('.ms-MessageBar--error');
    expect(messageBar).toBeInTheDocument();
  });

  it('renders additions details when usersToAdd > 0', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expect(screen.getByText('10')).toBeInTheDocument();
    expect(screen.getByText('50.00')).toBeInTheDocument();
  });

  it('renders removals details when usersToRemove > 0', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expect(screen.getByText('5')).toBeInTheDocument();
    expect(screen.getByText('25.00')).toBeInTheDocument();
  });

  it('hides additions section when usersToAdd is 0 and below threshold', () => {
    render(
      <ThresholdExceededActionDialogBase
        {...defaultProps}
        usersToAdd={0}
        increasePercentage={0}
      />
    );
    expect(screen.queryByText('users added')).not.toBeInTheDocument();
  });

  it('renders purge date when provided', () => {
    render(
      <ThresholdExceededActionDialogBase
        {...defaultProps}
        purgeDate="2026-06-01T00:00:00Z"
      />
    );
    const formatted = new Date('2026-06-01T00:00:00Z').toLocaleDateString();
    expect(screen.getByText(formatted)).toBeInTheDocument();
  });

  it('does not render purge date when not provided', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expect(screen.queryByText(/purged/)).not.toBeInTheDocument();
  });

  it('calls onDismiss when Cancel button is clicked', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    fireEvent.click(screen.getByText('Cancel'));
    expect(defaultProps.onDismiss).toHaveBeenCalledOnce();
  });

  it('fires onClick on Enter keydown for enabled action', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    const applyCard = screen.getByText('Apply Changes').closest('[role="button"]')!;
    fireEvent.keyDown(applyCard, { key: 'Enter' });
    expect(defaultProps.onApplyChanges).toHaveBeenCalledOnce();
  });

  it('fires onClick on Space keydown for enabled action', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    const applyCard = screen.getByText('Apply Changes').closest('[role="button"]')!;
    fireEvent.keyDown(applyCard, { key: ' ' });
    expect(defaultProps.onApplyChanges).toHaveBeenCalledOnce();
  });

  it('sets aria-disabled for disabled actions', () => {
    render(
      <ThresholdExceededActionDialogBase
        {...defaultProps}
        isPauseSyncEnabled={false}
      />
    );
    const pauseCard = screen.getByText('Pause Sync').closest('[role="button"]')!;
    expect(pauseCard.getAttribute('aria-disabled')).toBe('true');
    expect(pauseCard.getAttribute('tabindex')).toBe('-1');
  });
});
