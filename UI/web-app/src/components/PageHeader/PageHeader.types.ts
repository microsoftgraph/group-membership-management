// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type IStyle,
  type IStyleFunctionOrObject,
  type ITheme,
} from '@fluentui/react';
import type React from 'react';

export interface IPageHeaderStyles {
  root: IStyle;
  actionButtonsContainer: IStyle;
  backButton: IStyle;
  separator: IStyle;
}

export interface IPageHeaderStyleProps {
  className?: string;
  theme: ITheme;
}

export interface IPageHeaderProps
  extends React.AllHTMLAttributes<HTMLDivElement> {
  backButtonHidden?: boolean;
  onBackButtonClick?: () => void;
  rightButton?: React.ReactNode;
  
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<IPageHeaderStyleProps, IPageHeaderStyles>;
}
