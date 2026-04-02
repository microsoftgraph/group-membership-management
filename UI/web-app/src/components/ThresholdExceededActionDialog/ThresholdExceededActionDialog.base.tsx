// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import {
    classNamesFunction,
    Dialog,
    DialogType,
    DefaultButton,
    IProcessedStyleSet,
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
        thresholdPercentage,
        onApplyChanges,
        onEditRules,
        onEditThreshold,
        onPauseSync,
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
            title: modal.applyChanges,
            description: modal.applyChangesDescription,
            onClick: onApplyChanges,
        },
        {
            title: modal.editRules,
            description: modal.editRulesDescription,
            onClick: onEditRules,
        },
        {
            title: modal.editThreshold,
            description: modal.editThresholdDescription,
            onClick: onEditThreshold,
        },
        {
            title: modal.pauseSync,
            description: modal.pauseSyncDescription,
            onClick: onPauseSync,
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
                {isLoading ? (
                    <Spinner size={SpinnerSize.medium} />
                ) : (
                    <>
                        <p className={classNames.warningText}>
                            {format(modal.warningText, <strong>{groupName}</strong>)}
                        </p>
                        <p className={classNames.detailsText}>
                            {format(
                                modal.detailsText,
                                <strong>{usersToAdd}</strong>,
                                <strong>{increasePercentage.toFixed(2)}</strong>,
                                <strong>{thresholdPercentage}</strong>
                            )}
                        </p>
                        <div className={classNames.actionsGrid}>
                            {actions.map((action) => (
                                <div
                                    key={action.title}
                                    className={classNames.actionCard}
                                    onClick={action.onClick}
                                    role="button"
                                    tabIndex={0}
                                    onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') action.onClick(); }}
                                >
                                    <div className={classNames.actionCardTitle}>{action.title}</div>
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
