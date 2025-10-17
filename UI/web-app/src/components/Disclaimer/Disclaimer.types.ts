// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    type IStyle,
    type IStyleFunctionOrObject,
  } from '@fluentui/react';
import React, { ReactNode } from 'react';

export interface IDisclaimerStyles {
    root: IStyle;
    modalContainer: IStyle;
    buttonContainer: IStyle;
}

export interface IDisclaimerStyleProps {
    className?: string;
}

export interface IDisclaimerCheckbox {
    id: string;
    label: ReactNode;
}

export interface IDisclaimerProps {
    className?: string;
    styles?: IStyleFunctionOrObject<IDisclaimerStyleProps, IDisclaimerStyles>;
    checkboxes: IDisclaimerCheckbox[];
    onDismiss?: () => void;
}

export interface IDisclaimerStyleProps extends React.AllHTMLAttributes<HTMLDivElement>{
        /**
         * Optional className to apply to the root of the component.
         */
        className?: string;
      
        /**
         * Call to provide customized styling that will layer on top of the variant rules.
         */
        styles?: IStyleFunctionOrObject<IDisclaimerStyleProps, IDisclaimerStyles>;
};
