// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';

export type GeneralSettingStyles = {
  root: IStyle;
  card: IStyle;
  title: IStyle;
  description: IStyle;
};

export type GeneralSettingStyleProps = {
  className?: string;
  theme: ITheme;
};

export type GeneralSettingProps = React.AllHTMLAttributes<HTMLDivElement> & {
  className?: string;
  styles?: IStyleFunctionOrObject<GeneralSettingStyleProps, GeneralSettingStyles>;
  title: string;
  description: string;
};
