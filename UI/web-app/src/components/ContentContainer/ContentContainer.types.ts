// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
    IIconProps,
    type IStyle,
    type IStyleFunctionOrObject,
    type ITheme,
  } from '@fluentui/react';
  import type React from 'react';

  export interface IContentContainerStyles {
    root: IStyle;
    card: IStyle;
    cardHeader: IStyle;
    title: IStyle;
    linkButton: IStyle;
  }

  export interface IContentContainerStyleProps {
    className?: string;
    theme: ITheme;
  }

  export interface IContentContainerProps extends React.AllHTMLAttributes<HTMLDivElement> {
    /**
     * Optional className to apply to the root of the component.
     */
    className?: string;

    /**
     * Call to provide customized styling that will layer on top of the variant rules.
     */
    styles?: IStyleFunctionOrObject<IContentContainerStyleProps, IContentContainerStyles>;
    title: string;
    actionOnClick?: () => void;
    actionText?:  string | null;
    actionIcon?: IIconProps;
    useLinkButton?: boolean;
    linkButtonIconName?: string;
    hideSeparator?: boolean;
    removeButton?: boolean;
    editButton?: boolean;
  }


