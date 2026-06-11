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
    warningText: IStyle;
    detailsText: IStyle;
    actionsGrid: IStyle;
    actionCard: IStyle;
    actionCardEnabled: IStyle;
    actionCardTitle: IStyle;
    actionCardDescription: IStyle;
    footer: IStyle;
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
    onDismiss: () => void;
    groupName: string;
    usersToAdd: number;
    increasePercentage: number;
    thresholdPercentage: number;
    onApplyChanges: () => void;
    onEditRules: () => void;
    onEditThreshold: () => void;
    onPauseSync: () => void;
    isPauseSyncEnabled?: boolean;
}
