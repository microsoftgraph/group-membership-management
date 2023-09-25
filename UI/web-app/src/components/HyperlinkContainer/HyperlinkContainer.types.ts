// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    IIconProps,
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
  } from '@fluentui/react';
  import type React from 'react';

  export interface IHyperlinkContainerStyles {
    root: IStyle;
    card: IStyle;
    title: IStyle;
    description: IStyle;
    textFieldFieldGroup: IStyle;
  }

  export interface IHyperlinkContainerStyleProps {
    className?: string;
    theme: ITheme;
  }

  export interface IHyperlinkContainerProps extends React.AllHTMLAttributes<HTMLDivElement> {
    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;

    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IHyperlinkContainerStyleProps, IHyperlinkContainerStyles>;
    title: string;
    description: string;
    link: string;
    onUpdateLink: (link: string) => void;
    setHyperlinkError: (error: boolean) => void;
  }


