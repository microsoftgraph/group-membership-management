// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';

export type HyperlinkSettingStyles = {
  root: IStyle;
  card: IStyle;
  title: IStyle;
  description: IStyle;
  textFieldFieldGroup: IStyle;
};

export type HyperlinkSettingStyleProps = {
  className?: string;
  theme: ITheme;
};

export type HyperlinkSettingProps = React.AllHTMLAttributes<HTMLDivElement> & {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<HyperlinkSettingStyleProps, HyperlinkSettingStyles>;
  title: string;
  description: string;
  link: string;
  required?: boolean;
  onLinkChange: (link: string) => void;
  onValidation: (isValid: boolean) => void;
};
