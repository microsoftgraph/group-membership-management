// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';

export interface IPrivacyPolicyLinkStyles {
  root: IStyle;
  link: IStyle;
}

export interface IPrivacyPolicyLinkStyleProps {
  className?: string;
  theme: ITheme;
}

export interface IPrivacyPolicyLinkProps extends React.AllHTMLAttributes<HTMLDivElement> {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<IPrivacyPolicyLinkStyleProps, IPrivacyPolicyLinkStyles>;
}
