// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { ThresholdExceededActionDialogBase } from './ThresholdExceededActionDialog.base';
import { ThresholdExceededActionDialog } from './ThresholdExceededActionDialog';
import type { IThresholdExceededActionDialogProps } from './ThresholdExceededActionDialog.types';

vi.mock('../../store/hooks', () => ({
  useStrings: () => ({
    cancel: 'Cancel',
    close: 'Close',
    JobDetails: {
      Panel: {
        aiDescriptionLabel: 'Description (AI Generated)',
        aiDescriptionLoading: 'Generating explanation...',
        aiDescriptionError: 'Unable to generate explanation.',
        ThresholdExceededActionDialog: {
          title: 'Threshold exceeded',
          gracePeriodMessage:
            "You have until {0} to take action. After that, this group's affiliation with GMM will be removed.",
          bodyLeadIncrease:
            'This membership {0} because the configured {1}. Review the pending changes below and take action to continue.',
          bodyLeadDecrease:
            'This membership {0} because the configured {1}. Review the pending changes below and take action to continue.',
          bodyLeadBoth:
            'This membership {0} because the configured {1}. Review the pending changes below and take action to continue.',
          bodyLeadSyncWasPaused: 'sync was paused',
          bodyLeadIncreaseThresholdExceeded: 'increase threshold would be exceeded',
          bodyLeadDecreaseThresholdExceeded: 'decrease threshold would be exceeded',
          bodyLeadBothThresholdsExceeded: 'increase and decrease thresholds would be exceeded',
          statsTable: {
            membersAdded: 'Members added',
            membersRemoved: 'Members removed',
            rowLabelColumnHeader: 'Row label',
            columnHeaderLimit: 'Limit',
            columnHeaderActual: 'Actual',
            cellValueFormat: '{0} ({1}%)',
            breachAriaLabel: '{0} {1}, {2} percent — exceeds the {3} percent limit',
          },
          chooseAnActionHeader: 'Choose an action',
          applyChanges: 'Apply changes',
          applyChangesDescription: 'This looks fine. Allow this change to proceed on the next scheduled run.',
          editMembershipRules: 'Edit membership rules',
          editMembershipRulesDescription: 'Something look off? Check if your membership rules still match what you need.',
          disclosure: {
            headerCollapsed: 'Learn more about limits and change alerts',
            headerExpanded: 'What are membership change alerts?',
            explanation: 'Membership change alerts act as a safety net.',
            infoRow: 'Higher thresholds generate fewer alerts. Lower thresholds provide tighter control over membership changes.',
            currentSettingsLabel: 'Current settings:',
            increaseThresholdTitle: 'Increase threshold: {0}%',
            increaseThresholdSubtitle: 'Pause sync when additions exceed {0}%.',
            decreaseThresholdTitle: 'Decrease threshold: {0}%',
            decreaseThresholdSubtitle: 'Pause sync when removals exceed {0}%.',
            editAlertThresholdsButton: 'Edit alert thresholds',
          },
          alreadyResolvedMessage: 'This threshold alert was already resolved by {0} on {1}. No further action is required.',
          alreadyResolvedMessageNoActor: 'This threshold alert has already been resolved. No further action is required.',
        },
      },
    },
  }),
}));

// Both sides breach the threshold by default (50 > 30 additions, 25 > 20 removals).
const defaultProps: IThresholdExceededActionDialogProps = {
  isOpen: true,
  isLoading: false,
  usersToAdd: 10,
  increasePercentage: 50,
  thresholdPercentageForAdditions: 30,
  usersToRemove: 5,
  decreasePercentage: 25,
  thresholdPercentageForRemovals: 20,
  onApplyChanges: vi.fn(),
  onEditRules: vi.fn(),
  onEditAlertThresholds: vi.fn(),
  isApplyChangesEnabled: true,
  isEditRulesEnabled: true,
  isEditAlertThresholdsEnabled: true,
};

const expandDisclosure = (): void => {
  fireEvent.click(screen.getByRole('button', { name: 'Learn more about limits and change alerts' }));
};

const follows = (first: Element, second: Element): boolean =>
  Boolean(first.compareDocumentPosition(second) & Node.DOCUMENT_POSITION_FOLLOWING);

beforeEach(() => {
  vi.clearAllMocks();
});

describe('ThresholdExceededActionDialogBase', () => {
  it('renders the body content when isOpen is true', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expect(screen.getByText('Choose an action')).toBeInTheDocument();
  });

  it('does not render content when isOpen is false', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} isOpen={false} />);
    expect(screen.queryByText('Choose an action')).not.toBeInTheDocument();
  });

  it('shows a spinner when isLoading is true', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} isLoading={true} />);
    expect(document.querySelector('.ms-Spinner')).toBeInTheDocument();
  });

  it('renders the yellow grace-period MessageBar when purgeDate is supplied', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} purgeDate="2026-06-01T00:00:00Z" />);
    expect(document.querySelector('.ms-MessageBar--warning')).toBeInTheDocument();
  });

  it('does not render the yellow MessageBar when purgeDate is undefined', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expect(document.querySelector('.ms-MessageBar--warning')).not.toBeInTheDocument();
  });

  it('renders both red error and yellow grace-period MessageBars when both errorMessage and purgeDate are supplied', () => {
    render(
      <ThresholdExceededActionDialogBase
        {...defaultProps}
        errorMessage="Something went wrong"
        purgeDate="2026-06-01T00:00:00Z"
      />
    );
    expect(document.querySelector('.ms-MessageBar--error')).toBeInTheDocument();
    expect(document.querySelector('.ms-MessageBar--warning')).toBeInTheDocument();
  });

  it('renders error message bar when errorMessage is provided', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} errorMessage="Something went wrong" />);
    expect(document.querySelector('.ms-MessageBar--error')).toBeInTheDocument();
  });

  it('renders the body lead with bold spans directly below the MessageBar (no Pending changes section header is rendered)', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    const boldPaused = screen.getByText('sync was paused');
    expect(boldPaused.tagName).toBe('STRONG');
    expect(screen.getByText('increase and decrease thresholds would be exceeded').tagName).toBe('STRONG');
    expect(screen.queryByText('Pending changes')).not.toBeInTheDocument();
  });

  it('renders the stats table with two rows (Members added, Members removed) and three columns (row-label, Limit, Actual) regardless of which side(s) breached', () => {
    render(
      <ThresholdExceededActionDialogBase
        {...defaultProps}
        usersToAdd={0}
        increasePercentage={0}
        usersToRemove={0}
        decreasePercentage={0}
      />
    );
    expect(screen.getByText('Members added')).toBeInTheDocument();
    expect(screen.getByText('Members removed')).toBeInTheDocument();
    expect(screen.getByText('Row label')).toBeInTheDocument();
    expect(screen.getByText('Limit')).toBeInTheDocument();
    expect(screen.getByText('Actual')).toBeInTheDocument();
    expect(document.querySelectorAll('tbody tr')).toHaveLength(2);
  });

  it('Limit cell on each row renders <derived-count> (<thresholdPercentage>%) where derived-count = Math.round(changeQuantity × thresholdPercentage / changePercentage)', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    // additions: round(10 * 30 / 50) = 6; removals: round(5 * 20 / 25) = 4
    expect(screen.getByText('6 (30%)')).toBeInTheDocument();
    expect(screen.getByText('4 (20%)')).toBeInTheDocument();
  });

  it('Actual cell on each row renders <changeQuantity> (<changePercentage>%) with .toFixed(1) on the percentage', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expect(screen.getByText('10 (50.0%)')).toBeInTheDocument();
    expect(screen.getByText('5 (25.0%)')).toBeInTheDocument();
  });

  it('Actual cell on the additions row carries the breach aria-label iff increasePercentage > thresholdPercentageForAdditions', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} decreasePercentage={10} />);
    expect(
      screen.getByLabelText('Members added 10, 50.0 percent — exceeds the 30 percent limit')
    ).toBeInTheDocument();
    expect(screen.queryByLabelText(/Members removed .* exceeds/)).not.toBeInTheDocument();
  });

  it('Actual cell on the removals row carries the breach aria-label iff decreasePercentage > thresholdPercentageForRemovals', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} increasePercentage={10} />);
    expect(
      screen.getByLabelText('Members removed 5, 25.0 percent — exceeds the 20 percent limit')
    ).toBeInTheDocument();
    expect(screen.queryByLabelText(/Members added .* exceeds/)).not.toBeInTheDocument();
  });

  it('both Actual cells carry the breach aria-label when both sides breached', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expect(
      screen.getByLabelText('Members added 10, 50.0 percent — exceeds the 30 percent limit')
    ).toBeInTheDocument();
    expect(
      screen.getByLabelText('Members removed 5, 25.0 percent — exceeds the 20 percent limit')
    ).toBeInTheDocument();
  });

  it('renders exactly two action cards (no Pause sync, no Edit alert threshold in the action grid)', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expect(screen.getByRole('button', { name: 'Apply changes' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Edit membership rules' })).toBeInTheDocument();
    expect(screen.queryByText('Pause sync')).not.toBeInTheDocument();
    // The disclosure panel is collapsed, so its 'Edit alert thresholds' button is not rendered.
    expect(screen.queryByText('Edit alert thresholds')).not.toBeInTheDocument();
  });

  it('clicking the inner Apply changes button invokes onApplyChanges', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    fireEvent.click(screen.getByRole('button', { name: 'Apply changes' }));
    expect(defaultProps.onApplyChanges).toHaveBeenCalledOnce();
  });

  it('clicking the inner Edit membership rules button invokes onEditRules', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    fireEvent.click(screen.getByRole('button', { name: 'Edit membership rules' }));
    expect(defaultProps.onEditRules).toHaveBeenCalledOnce();
  });

  it('does not invoke onApplyChanges when the Apply changes button is disabled', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} isApplyChangesEnabled={false} />);
    const button = screen.getByRole('button', { name: 'Apply changes' });
    expect(button).toBeDisabled();
    fireEvent.click(button);
    expect(defaultProps.onApplyChanges).not.toHaveBeenCalled();
  });

  it('does not render a bottom Cancel button', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expect(screen.queryByText('Cancel')).not.toBeInTheDocument();
  });

  it("renders the disclosure panel collapsed by default with header label 'Learn more about limits and change alerts'", () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    const header = screen.getByRole('button', { name: 'Learn more about limits and change alerts' });
    expect(header).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByText('Current settings:')).not.toBeInTheDocument();
  });

  it("clicking the disclosure header expands the panel, flips the header label to 'What are membership change alerts?', and reveals the Edit alert thresholds button", () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expandDisclosure();
    const header = screen.getByRole('button', { name: 'What are membership change alerts?' });
    expect(header).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('button', { name: 'Edit alert thresholds' })).toBeInTheDocument();
  });

  it("renders the disclosure panel directly below the stats table and above the 'Choose an action' section header", () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    const table = document.querySelector('table')!;
    const disclosureHeader = screen.getByRole('button', { name: 'Learn more about limits and change alerts' });
    const chooseAnAction = screen.getByText('Choose an action');
    expect(follows(table, disclosureHeader)).toBe(true);
    expect(follows(disclosureHeader, chooseAnAction)).toBe(true);
  });

  it('renders Increase threshold and Decrease threshold cards with the supplied percentages', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expandDisclosure();
    expect(screen.getByText('Increase threshold: 30%')).toBeInTheDocument();
    expect(screen.getByText('Decrease threshold: 20%')).toBeInTheDocument();
  });

  it("renders expanded disclosure content in vertical order: explanation → 'Current settings:' → threshold cards → bottom row", () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expandDisclosure();
    const explanation = screen.getByText('Membership change alerts act as a safety net.');
    const currentSettings = screen.getByText('Current settings:');
    const increaseCard = screen.getByText('Increase threshold: 30%');
    const infoRow = screen.getByText(/Higher thresholds generate fewer alerts/);
    expect(follows(explanation, currentSettings)).toBe(true);
    expect(follows(currentSettings, increaseCard)).toBe(true);
    expect(follows(increaseCard, infoRow)).toBe(true);
  });

  it("places the info text and the 'Edit alert thresholds' button on the bottom row (info text is not rendered above 'Current settings:')", () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expandDisclosure();
    const currentSettings = screen.getByText('Current settings:');
    const infoRow = screen.getByText(/Higher thresholds generate fewer alerts/);
    const editButton = screen.getByRole('button', { name: 'Edit alert thresholds' });
    expect(follows(currentSettings, infoRow)).toBe(true);
    expect(follows(infoRow, editButton)).toBe(true);
  });

  it('clicking the Edit alert thresholds button invokes onEditAlertThresholds', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expandDisclosure();
    fireEvent.click(screen.getByRole('button', { name: 'Edit alert thresholds' }));
    expect(defaultProps.onEditAlertThresholds).toHaveBeenCalledOnce();
  });

  it('renders the Edit alert thresholds button disabled when isEditAlertThresholdsEnabled is false', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} isEditAlertThresholdsEnabled={false} />);
    expandDisclosure();
    expect(screen.getByRole('button', { name: 'Edit alert thresholds' })).toBeDisabled();
  });

  // jsdom doesn't render <MessageBar> text, so assert the severity class + action gating instead.
  it('renders the green success already-resolved MessageBar when isResolved with a known resolver', () => {
    render(
      <ThresholdExceededActionDialogBase
        {...defaultProps}
        isResolved={true}
        resolvedBy="alice@contoso.com"
        resolvedTime="2026-06-01T00:00:00Z"
      />
    );
    expect(document.querySelector('.ms-MessageBar--success')).toBeInTheDocument();
  });

  it('renders the green success already-resolved MessageBar when isResolved even without a known resolver', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} isResolved={true} />);
    expect(document.querySelector('.ms-MessageBar--success')).toBeInTheDocument();
  });

  it('does not render the success already-resolved MessageBar when isResolved is false', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expect(document.querySelector('.ms-MessageBar--success')).not.toBeInTheDocument();
  });

  it('disables the Apply changes button when isResolved even if isApplyChangesEnabled is true', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} isApplyChangesEnabled={true} isResolved={true} />);
    expect(screen.getByRole('button', { name: 'Apply changes' })).toBeDisabled();
  });

  it('suppresses the yellow grace-period MessageBar when isResolved even if purgeDate is supplied', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} isResolved={true} purgeDate="2026-06-01T00:00:00Z" />);
    expect(document.querySelector('.ms-MessageBar--success')).toBeInTheDocument();
    expect(document.querySelector('.ms-MessageBar--warning')).not.toBeInTheDocument();
  });
});

describe('ThresholdExceededActionDialog (styled) row banding', () => {
  it('applies the row-banding style class to the <thead> row and both body rows', () => {
    render(<ThresholdExceededActionDialog {...defaultProps} />);
    const theadRow = document.querySelector('thead tr')!;
    const addedRow = screen.getByText('Members added').closest('tr')!;
    const removedRow = screen.getByText('Members removed').closest('tr')!;
    expect(theadRow.className).toBeTruthy();
    expect(addedRow.className).toBeTruthy();
    expect(addedRow.className).toBe(theadRow.className);
    expect(removedRow.className).toBe(theadRow.className);
  });
});

describe('ThresholdExceededActionDialogBase AI description', () => {
  it('does not render the AI description panel when no description data is provided', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} />);
    expect(screen.queryByText('Description (AI Generated)')).not.toBeInTheDocument();
  });

  it('renders the loading state while the AI description is being generated', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} isAiDescriptionLoading />);
    expect(screen.getByText('Description (AI Generated)')).toBeInTheDocument();
    expect(screen.getByText('Generating explanation...')).toBeInTheDocument();
  });

  it('renders the error state when the AI description fails to generate', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} aiDescriptionError />);
    expect(screen.getByText('Description (AI Generated)')).toBeInTheDocument();
    expect(screen.getByText('Unable to generate explanation.')).toBeInTheDocument();
  });

  it('renders the AI description text when provided', () => {
    render(<ThresholdExceededActionDialogBase {...defaultProps} aiDescription="This sync exceeded the threshold." />);
    expect(screen.getByText('Description (AI Generated)')).toBeInTheDocument();
    expect(screen.getByText('This sync exceeded the threshold.')).toBeInTheDocument();
  });

  it('prefers the loading state over any cached description text', () => {
    render(
      <ThresholdExceededActionDialogBase {...defaultProps} isAiDescriptionLoading aiDescription="stale text" />
    );
    expect(screen.getByText('Generating explanation...')).toBeInTheDocument();
    expect(screen.queryByText('stale text')).not.toBeInTheDocument();
  });
});
