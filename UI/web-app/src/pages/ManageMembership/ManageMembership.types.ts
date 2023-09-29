// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
} from '@fluentui/react';
import type React from 'react';

export interface IManageMembershipStyles {
    root: IStyle;
    card: IStyle;
    title: IStyle;
    stepTitle: IStyle;
    stepDescription: IStyle;
    dropdownTitle: IStyle;
    dropdownField: IStyle;
    bottomContainer: IStyle;
    circlesContainer: IStyle;
    circleIcon: IStyle;
    nextButtonContainer: IStyle;
    endpointsContainer: IStyle;
    outlookWarning: IStyle;
    outlookContainer: IStyle;
}

export interface IManageMembershipStyleProps {
    className?: string;
    theme: ITheme;
}

export interface IManageMembershipProps
    extends React.AllHTMLAttributes<HTMLDivElement> {
    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;

    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IManageMembershipStyleProps, IManageMembershipStyles>;
}
