// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type IStyle,
  type IStyleFunctionOrObject,
  type ITheme,
} from '@fluentui/react';
import type React from 'react';

export interface IBannerStyles {
  root: IStyle;
  icon: IStyle;
  message: IStyle;
  messageContainer: IStyle;
  toggle: IStyle;
}

export interface IBannerStyleProps {
  collapsed: boolean;
  className?: string;
  theme: ITheme;
}

export interface IBannerProps
  extends React.AllHTMLAttributes<HTMLDivElement> {
  
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<IBannerStyleProps, IBannerStyles>;
}
