// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import {
  type IStyle,
  type IStyleFunctionOrObject,
  type ITheme,
} from '@fluentui/react';
import type React from 'react';

export interface IAppHeaderStyles {
  root: IStyle;
  mainButton: IStyle;
  appIcon: IStyle;
  appTitle: IStyle;
  settingsContainer: IStyle;
  settingsIcon: IStyle;
  titleContainer: IStyle;
  userPersona: IStyle;
}

export interface IAppHeaderStyleProps {
  className?: string;
  theme: ITheme;
}

export interface IAppHeaderProps
  extends React.AllHTMLAttributes<HTMLDivElement> {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<IAppHeaderStyleProps, IAppHeaderStyles>;
}
