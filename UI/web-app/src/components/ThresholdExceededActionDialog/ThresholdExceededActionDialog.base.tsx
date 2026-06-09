// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import {
    classNamesFunction,
    Dialog,
    DialogType,
    DefaultButton,
    Icon,
    IProcessedStyleSet,
    MessageBar,
    MessageBarType,
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

export const ThresholdExceededActionDialogBase: React.FunctionComponent<IThresholdExceededActionDialogProps> = (
    props: IThresholdExceededActionDialogProps
) => {
    const {
        className,
        styles,
        isOpen,
        isLoading,
        onDismiss,
        groupName,
        usersToAdd,
        increasePercentage,
        thresholdPercentageForAdditions,
        usersToRemove,
        decreasePercentage,
        thresholdPercentageForRemovals,
        onApplyChanges,
        onEditRules,
        onEditThreshold,
        onPauseSync,
        isApplyChangesEnabled = false,
        isEditRulesEnabled = false,
        isEditThresholdEnabled = false,
        isPauseSyncEnabled = false,
        errorMessage,
        purgeDate,
    } = props;

    const theme = useTheme();
    const strings = useStrings();
    const modal = strings.JobDetails.Panel.ThresholdExceededActionDialog;

    const classNames: IProcessedStyleSet<IThresholdExceededActionDialogStyles> = getClassNames(styles, {
        className,
        theme,
    });

    const actions = [
        {
            icon: 'Completed',
            title: modal.applyChanges,
            description: modal.applyChangesDescription as React.ReactNode,
            onClick: onApplyChanges,
            enabled: isApplyChangesEnabled,
        },
        {
            icon: 'Edit',
            title: modal.editRules,
            description: modal.editRulesDescription as React.ReactNode,
            onClick: onEditRules,
            enabled: isEditRulesEnabled,
        },
        {
            icon: 'Edit',
            title: modal.editThreshold,
            description: format(
                modal.editThresholdDescription,
                <strong>{thresholdPercentageForAdditions}%</strong>,
                <strong>{thresholdPercentageForRemovals}%</strong>
            ) as React.ReactNode,
            onClick: onEditThreshold,
            enabled: isEditThresholdEnabled,
        },
        {
            icon: 'Pause',
            title: modal.pauseSync,
            description: modal.pauseSyncDescription as React.ReactNode,
            onClick: onPauseSync,
            enabled: isPauseSyncEnabled,
        },
    ];

    return (
        <Dialog
            hidden={!isOpen}
            onDismiss={onDismiss}
            dialogContentProps={{
                type: DialogType.close,
                title: modal.title,
            }}
            modalProps={{
                isBlocking: false,
            }}
            minWidth={540}
        >
            <div className={classNames.root}>
                {errorMessage && (
                    <MessageBar messageBarType={MessageBarType.error}>
                        {errorMessage}
                    </MessageBar>
                )}
                {isLoading ? (
                    <Spinner size={SpinnerSize.medium} />
                ) : (
                    <>
                        <p className={classNames.warningText}>
                            {format(modal.warningText, <strong>{groupName}</strong>)}
                        </p>
                        {(usersToAdd > 0 || increasePercentage > thresholdPercentageForAdditions) && (
                            <p className={classNames.detailsText}>
                                {format(
                                    modal.additionsDetailsText,
                                    <strong>{usersToAdd}</strong>,
                                    <strong>{increasePercentage.toFixed(2)}</strong>,
                                    <strong>{thresholdPercentageForAdditions}</strong>
                                )}
                            </p>
                        )}
                        {(usersToRemove > 0 || decreasePercentage > thresholdPercentageForRemovals) && (
                            <p className={classNames.detailsText}>
                                {format(
                                    modal.removalsDetailsText,
                                    <strong>{usersToRemove}</strong>,
                                    <strong>{decreasePercentage.toFixed(2)}</strong>,
                                    <strong>{thresholdPercentageForRemovals}</strong>
                                )}
                            </p>
                        )}
                        {purgeDate && (
                            <p className={classNames.detailsText}>
                                {format(modal.purgeDateText, <strong>{new Date(purgeDate).toLocaleDateString()}</strong>)}
                            </p>
                        )}
                        <div className={classNames.actionsGrid}>
                            {actions.map((action) => (
                                <div
                                    key={action.title}
                                    className={action.enabled ? classNames.actionCardEnabled : classNames.actionCard}
                                    onClick={action.enabled ? action.onClick : undefined}
                                    role="button"
                                    tabIndex={action.enabled ? 0 : -1}
                                    aria-disabled={!action.enabled}
                                    onKeyDown={action.enabled ? (e) => {
                                        if (e.key === 'Enter') {
                                            action.onClick();
                                        } else if (e.key === ' ' && !e.repeat) {
                                            e.preventDefault();
                                            action.onClick();
                                        }
                                    } : undefined}
                                >
                                    <div className={classNames.actionCardTitleContainer}>
                                        <Icon iconName={action.icon} className={classNames.actionCardIcon} />
                                        <div className={classNames.actionCardTitle}>{action.title}</div>
                                    </div>
                                    <div className={classNames.actionCardDescription}>{action.description}</div>
                                </div>
                            ))}
                        </div>
                    </>
                )}
                <div className={classNames.footer}>
                    <DefaultButton text={strings.cancel} onClick={onDismiss} />
                </div>
            </div>
        </Dialog>
    );
};
