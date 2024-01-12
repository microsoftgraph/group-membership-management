// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';

export type MembershipConfigurationStyles = {
  root: IStyle;
  addButtonContainer: IStyle;
  toggleContainer: IStyle;
  card: IStyle;
};

export type MembershipConfigurationStyleProps = {
  className?: string;
  theme: ITheme;
};

export type MembershipConfigurationProps = React.AllHTMLAttributes<HTMLDivElement> & {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<MembershipConfigurationStyleProps, MembershipConfigurationStyles>;
};

export type MembershipConfigurationViewProps = MembershipConfigurationProps & {
};
