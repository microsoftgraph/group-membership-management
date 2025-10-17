// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
} from '@fluentui/react';
import type React from 'react';

export interface IJobHistoryPanelStyles {
    root: IStyle;
    container: IStyle;
    header: IStyle;
}

export interface IJobHistoryPanelStyleProps {
    className?: string;
    theme: ITheme;
}

export interface IJobHistoryPanelProps extends React.AllHTMLAttributes<HTMLElement> {
    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;
    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IJobHistoryPanelStyleProps, IJobHistoryPanelStyles>;
    isOpen: boolean;
    dismissPanel: () => void;
    jobId: string;
};