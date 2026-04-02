// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
} from '@fluentui/react';
import type React from 'react';

export interface ITakeActionModalStyles {
    root: IStyle;
    warningText: IStyle;
    detailsText: IStyle;
    actionsGrid: IStyle;
    actionCard: IStyle;
    actionCardTitle: IStyle;
    actionCardDescription: IStyle;
    footer: IStyle;
}

export interface ITakeActionModalStyleProps {
    className?: string;
    theme: ITheme;
}

export interface ITakeActionModalProps extends React.AllHTMLAttributes<HTMLElement> {
    className?: string;
    styles?: IStyleFunctionOrObject<ITakeActionModalStyleProps, ITakeActionModalStyles>;
    isOpen: boolean;
    onDismiss: () => void;
    groupName: string;
    usersToAdd: number;
    increasePercentage: number;
    thresholdPercentage: number;
    onApplyChanges: () => void;
    onEditRules: () => void;
    onEditThreshold: () => void;
    onPauseSync: () => void;
}
