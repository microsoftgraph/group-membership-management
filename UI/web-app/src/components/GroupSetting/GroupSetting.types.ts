// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';

export interface IGroupSettingStyles {
    root: IStyle;
    textField: IStyle;
    textFieldGroup: IStyle;
    labelContainer: IStyle;
    suggestionItems: IStyle;
    toggleContainer: IStyle;
}

export interface IGroupSettingStyleProps {
    className?: string;
    theme: ITheme;
}

export interface IGroupSettingProps extends React.AllHTMLAttributes<HTMLDivElement> {
    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;

    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IGroupSettingStyleProps, IGroupSettingStyles>;
};
