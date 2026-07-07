// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
} from '@fluentui/react';
import type React from 'react';

export interface IThresholdExceededActionDialogStyles {
    root: IStyle;
    headerDivider: IStyle;
    errorMessageBar: IStyle;
    gracePeriodMessageBar: IStyle;
    bodyLead: IStyle;
    aiDescriptionPanel: IStyle;
    aiDescriptionLabel: IStyle;
    aiDescriptionText: IStyle;
    statsTable: IStyle;
    statsTableHeaderCell: IStyle;
    statsTableRowLabelCell: IStyle;
    statsTableLimitCell: IStyle;
    statsTableActualCell: IStyle;
    statsTableActualCellBreached: IStyle;
    statsTableBandedRow: IStyle;
    disclosurePanel: IStyle;
    disclosureHeader: IStyle;
    disclosureChevron: IStyle;
    disclosureBody: IStyle;
    disclosureExplanation: IStyle;
    currentSettingsLabel: IStyle;
    currentSettingsRow: IStyle;
    currentSettingCard: IStyle;
    currentSettingIcon: IStyle;
    currentSettingTitle: IStyle;
    currentSettingSubtitle: IStyle;
    disclosureBottomRow: IStyle;
    disclosureInfoText: IStyle;
    editThresholdsButton: IStyle;
    chooseAnActionHeader: IStyle;
    actionsGrid: IStyle;
    actionCard: IStyle;
    actionCardTitleContainer: IStyle;
    actionCardIcon: IStyle;
    actionCardTitle: IStyle;
    actionCardDescription: IStyle;
    actionCardButton: IStyle;
}

export interface IThresholdExceededActionDialogStyleProps {
    className?: string;
    theme: ITheme;
}

export interface IThresholdExceededActionDialogProps extends React.AllHTMLAttributes<HTMLElement> {
    className?: string;
    styles?: IStyleFunctionOrObject<IThresholdExceededActionDialogStyleProps, IThresholdExceededActionDialogStyles>;
    isOpen: boolean;
    isLoading: boolean;
    usersToAdd: number;
    increasePercentage: number;
    thresholdPercentageForAdditions: number;
    usersToRemove: number;
    decreasePercentage: number;
    thresholdPercentageForRemovals: number;
    onApplyChanges: () => void;
    onEditRules: () => void;
    onEditAlertThresholds: () => void;
    isApplyChangesEnabled?: boolean;
    isEditRulesEnabled?: boolean;
    isEditAlertThresholdsEnabled?: boolean;
    errorMessage?: string;
    purgeDate?: string;
    aiDescription?: string;
    isAiDescriptionLoading?: boolean;
    aiDescriptionError?: boolean;
}
