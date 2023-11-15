// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';
import type { SettingName } from '../../models';
import type { IStrings } from '../../services/localization';

export type AdminConfigStyles = {
  root: IStyle;
  card: IStyle;
  title: IStyle;
  description: IStyle;
  tiles: IStyle;
  bottomContainer: IStyle;
};

export type AdminConfigStyleProps = {
  className?: string;
  theme: ITheme;
};

export type AdminConfigProps = React.AllHTMLAttributes<HTMLDivElement> & {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<AdminConfigStyleProps, AdminConfigStyles>;
};

export type AdminConfigViewProps = AdminConfigProps & {
  isSaving: boolean;
  onSave: (settings: { readonly [key in SettingName]: string }) => void;
  settings: { readonly [key in SettingName]: string };
  strings: IStrings['AdminConfig'];
};
