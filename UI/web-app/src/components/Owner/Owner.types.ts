// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { IStyle, IStyleFunctionOrObject, ITheme } from '@fluentui/react';

export interface IOwnerStyles {
  root: IStyle;
}

export interface IOwnerStyleProps {
  className?: string;
  theme: ITheme;
}

export interface IOwnerProps extends React.AllHTMLAttributes<HTMLDivElement> {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<IOwnerStyleProps, IOwnerStyles>;
}