// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useState } from 'react';
import {
    classNamesFunction,
    DefaultButton,
    Icon,
    IProcessedStyleSet,
    MessageBar,
    MessageBarType,
    PrimaryButton,
    Spinner,
    SpinnerSize,
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { format } from 'react-string-format';
import { useStrings } from '../../store/hooks';
import type {
    IThresholdExceededActionDialogProps,
    IThresholdExceededActionDialogStyleProps,
    IThresholdExceededActionDialogStyles,
} from './ThresholdExceededActionDialog.types';

const getClassNames = classNamesFunction<IThresholdExceededActionDialogStyleProps, IThresholdExceededActionDialogStyles>();

const visuallyHidden: React.CSSProperties = {
    position: 'absolute',
    width: 1,
    height: 1,
    padding: 0,
    margin: -1,
    overflow: 'hidden',
    clip: 'rect(0 0 0 0)',
    whiteSpace: 'nowrap',
    border: 0,
};

// Substitutes {0}, {1}, ... placeholders with the supplied primitive arguments and
// returns a plain string (used for cell text and aria-labels, which must be strings).
const formatString = (template: string, ...args: (string | number)[]): string =>
    template.replace(/\{(\d+)\}/g, (_match, index) => String(args[Number(index)] ?? ''));

export const ThresholdExceededActionDialogBase: React.FunctionComponent<IThresholdExceededActionDialogProps> = (
    props: IThresholdExceededActionDialogProps
) => {
    const {
        className,
        styles,
        isOpen,
        isLoading,
        usersToAdd,
        increasePercentage,
        thresholdPercentageForAdditions,
        usersToRemove,
        decreasePercentage,
        thresholdPercentageForRemovals,
        onApplyChanges,
        onEditRules,
        onEditAlertThresholds,
        isApplyChangesEnabled = false,
        isEditRulesEnabled = false,
        isEditAlertThresholdsEnabled = false,
        errorMessage,
        purgeDate,
        aiDescription,
        isAiDescriptionLoading = false,
        aiDescriptionError = false,
        membershipLookup,
    } = props;

    const theme = useTheme();
    const strings = useStrings();
    const modal = strings.JobDetails.Panel.ThresholdExceededActionDialog;

    const classNames: IProcessedStyleSet<IThresholdExceededActionDialogStyles> = getClassNames(styles, {
        className,
        theme,
    });

    // The Fluent Dialog stays mounted and toggles `hidden` rather than unmounting on close,
    // so force the disclosure panel back to collapsed on each reopen.
    const [isAlertsInfoOpen, setIsAlertsInfoOpen] = useState(false);
    useEffect(() => {
        if (isOpen) {
            setIsAlertsInfoOpen(false);
        }
    }, [isOpen]);

    const additionsBreached = increasePercentage > thresholdPercentageForAdditions;
    const removalsBreached = decreasePercentage > thresholdPercentageForRemovals;
    const bothBreached = additionsBreached && removalsBreached;

    const bodyLeadTemplate = bothBreached
        ? modal.bodyLeadBoth
        : additionsBreached
            ? modal.bodyLeadIncrease
            : modal.bodyLeadDecrease;
    const bodyLeadPhrase = bothBreached
        ? modal.bodyLeadBothThresholdsExceeded
        : additionsBreached
            ? modal.bodyLeadIncreaseThresholdExceeded
            : modal.bodyLeadDecreaseThresholdExceeded;

    const statsRows = [
        {
            key: 'additions',
            label: modal.statsTable.membersAdded,
            changeQuantity: usersToAdd,
            changePercentage: increasePercentage,
            thresholdPercentage: thresholdPercentageForAdditions,
            breached: additionsBreached,
            banded: true,
        },
        {
            key: 'removals',
            label: modal.statsTable.membersRemoved,
            changeQuantity: usersToRemove,
            changePercentage: decreasePercentage,
            thresholdPercentage: thresholdPercentageForRemovals,
            breached: removalsBreached,
            banded: true,
        },
    ];

    if (!isOpen) {
        return null;
    }

    return (
        <div className={classNames.root}>
            <div className={classNames.headerDivider} />
            {errorMessage && (
                <MessageBar className={classNames.errorMessageBar} messageBarType={MessageBarType.error}>
                    {errorMessage}
                </MessageBar>
            )}
            {isLoading ? (
                <Spinner size={SpinnerSize.medium} />
            ) : (
                    <>
                        {purgeDate && (
                            <MessageBar className={classNames.gracePeriodMessageBar} messageBarType={MessageBarType.warning}>
                                {format(
                                    modal.gracePeriodMessage,
                                    <strong>
                                        {new Date(purgeDate).toLocaleDateString(undefined, {
                                            weekday: 'short',
                                            year: 'numeric',
                                            month: 'short',
                                            day: 'numeric',
                                        })}
                                    </strong>
                                )}
                            </MessageBar>
                        )}
                        <p className={classNames.bodyLead}>
                            {format(
                                bodyLeadTemplate,
                                <strong>{modal.bodyLeadSyncWasPaused}</strong>,
                                <strong>{bodyLeadPhrase}</strong>
                            )}
                        </p>
                        {(isAiDescriptionLoading || aiDescriptionError || aiDescription) && (
                            <div className={classNames.aiDescriptionPanel}>
                                <span className={classNames.aiDescriptionLabel}>
                                    {strings.JobDetails.Panel.aiDescriptionLabel}
                                </span>
                                {isAiDescriptionLoading ? (
                                    <Spinner size={SpinnerSize.small} label={strings.JobDetails.Panel.aiDescriptionLoading} labelPosition="right" />
                                ) : aiDescriptionError ? (
                                    <span className={classNames.aiDescriptionText}>{strings.JobDetails.Panel.aiDescriptionError}</span>
                                ) : (
                                    <span className={classNames.aiDescriptionText}>{aiDescription}</span>
                                )}
                            </div>
                        )}
                        <table className={classNames.statsTable}>
                            <thead>
                                <tr className={classNames.statsTableBandedRow}>
                                    <th scope="col" className={classNames.statsTableHeaderCell}>
                                        <span style={visuallyHidden}>{modal.statsTable.rowLabelColumnHeader}</span>
                                    </th>
                                    <th scope="col" className={classNames.statsTableHeaderCell}>
                                        {modal.statsTable.columnHeaderLimit}
                                    </th>
                                    <th scope="col" className={classNames.statsTableHeaderCell}>
                                        {modal.statsTable.columnHeaderActual}
                                    </th>
                                </tr>
                            </thead>
                            <tbody>
                                {statsRows.map((row) => {
                                    const limitCount =
                                        row.changePercentage > 0
                                            ? Math.round((row.changeQuantity * row.thresholdPercentage) / row.changePercentage)
                                            : 0;
                                    const breachAriaLabel = row.breached
                                        ? formatString(
                                            modal.statsTable.breachAriaLabel,
                                            row.label,
                                            row.changeQuantity,
                                            row.changePercentage.toFixed(1),
                                            row.thresholdPercentage
                                        )
                                        : undefined;
                                    return (
                                        <tr key={row.key} className={row.banded ? classNames.statsTableBandedRow : undefined}>
                                            <th scope="row" className={classNames.statsTableRowLabelCell}>
                                                {row.label}
                                            </th>
                                            <td className={classNames.statsTableLimitCell}>
                                                {formatString(modal.statsTable.cellValueFormat, limitCount, row.thresholdPercentage)}
                                            </td>
                                            <td className={classNames.statsTableActualCell} aria-label={breachAriaLabel}>
                                                <span className={row.breached ? classNames.statsTableActualCellBreached : undefined}>
                                                    {formatString(
                                                        modal.statsTable.cellValueFormat,
                                                        row.changeQuantity,
                                                        row.changePercentage.toFixed(1)
                                                    )}
                                                </span>
                                            </td>
                                        </tr>
                                    );
                                })}
                            </tbody>
                        </table>
                        <div className={classNames.disclosurePanel}>
                            <button
                                type="button"
                                className={classNames.disclosureHeader}
                                style={{ fontWeight: isAlertsInfoOpen ? 600 : 400 }}
                                aria-expanded={isAlertsInfoOpen}
                                onClick={() => setIsAlertsInfoOpen((prev) => !prev)}
                            >
                                <span>{isAlertsInfoOpen ? modal.disclosure.headerExpanded : modal.disclosure.headerCollapsed}</span>
                                <Icon
                                    className={classNames.disclosureChevron}
                                    iconName={isAlertsInfoOpen ? 'ChevronUp' : 'ChevronDown'}
                                />
                            </button>
                            {isAlertsInfoOpen && (
                                <div className={classNames.disclosureBody}>
                                    <p className={classNames.disclosureExplanation}>{modal.disclosure.explanation}</p>
                                    <div className={classNames.currentSettingsLabel}>{modal.disclosure.currentSettingsLabel}</div>
                                    <div className={classNames.currentSettingsRow}>
                                        <div className={classNames.currentSettingCard}>
                                            <span className={classNames.currentSettingIcon} style={{ display: 'inline-flex', alignItems: 'center', gap: 2 }}>
                                                <Icon iconName="People" />
                                                <Icon iconName="CalculatorAddition" style={{ fontSize: 10 }} />
                                            </span>
                                            <div>
                                                <div className={classNames.currentSettingTitle}>
                                                    {formatString(modal.disclosure.increaseThresholdTitle, thresholdPercentageForAdditions)}
                                                </div>
                                                <div className={classNames.currentSettingSubtitle}>
                                                    {formatString(modal.disclosure.increaseThresholdSubtitle, thresholdPercentageForAdditions)}
                                                </div>
                                            </div>
                                        </div>
                                        <div className={classNames.currentSettingCard}>
                                            <span className={classNames.currentSettingIcon} style={{ display: 'inline-flex', alignItems: 'center', gap: 2 }}>
                                                <Icon iconName="People" />
                                                <Icon iconName="CalculatorSubtract" style={{ fontSize: 10 }} />
                                            </span>
                                            <div>
                                                <div className={classNames.currentSettingTitle}>
                                                    {formatString(modal.disclosure.decreaseThresholdTitle, thresholdPercentageForRemovals)}
                                                </div>
                                                <div className={classNames.currentSettingSubtitle}>
                                                    {formatString(modal.disclosure.decreaseThresholdSubtitle, thresholdPercentageForRemovals)}
                                                </div>
                                            </div>
                                        </div>
                                    </div>
                                    <div className={classNames.disclosureBottomRow}>
                                        <div className={classNames.disclosureInfoText}>
                                            <Icon iconName="Info" />
                                            <span>{modal.disclosure.infoRow}</span>
                                        </div>
                                        <PrimaryButton
                                            className={classNames.editThresholdsButton}
                                            text={modal.disclosure.editAlertThresholdsButton}
                                            disabled={!isEditAlertThresholdsEnabled}
                                            onClick={onEditAlertThresholds}
                                        />
                                    </div>
                                </div>
                            )}
                        </div>
                        {membershipLookup}
                        <h3 className={classNames.chooseAnActionHeader}>{modal.chooseAnActionHeader}</h3>
                        <div className={classNames.actionsGrid}>
                            <div className={classNames.actionCard}>
                                <div className={classNames.actionCardTitleContainer}>
                                    <Icon iconName="Completed" className={classNames.actionCardIcon} />
                                    <div className={classNames.actionCardTitle}>{modal.applyChanges}</div>
                                </div>
                                <div className={classNames.actionCardDescription}>{modal.applyChangesDescription}</div>
                                <PrimaryButton
                                    className={classNames.actionCardButton}
                                    text={modal.applyChanges}
                                    disabled={!isApplyChangesEnabled}
                                    onClick={onApplyChanges}
                                />
                            </div>
                            <div className={classNames.actionCard}>
                                <div className={classNames.actionCardTitleContainer}>
                                    <Icon iconName="Edit" className={classNames.actionCardIcon} />
                                    <div className={classNames.actionCardTitle}>{modal.editMembershipRules}</div>
                                </div>
                                <div className={classNames.actionCardDescription}>{modal.editMembershipRulesDescription}</div>
                                <DefaultButton
                                    className={classNames.actionCardButton}
                                    text={modal.editMembershipRules}
                                    disabled={!isEditRulesEnabled}
                                    onClick={onEditRules}
                                />
                            </div>
                        </div>
                    </>
                )}
        </div>
    );
};
