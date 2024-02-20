// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';
import { HRSourcePartSource } from '../../models/HRSourcePart';

export type HRQuerySourceStyles = {
  root: IStyle;
  textFieldGroup: IStyle;
  textField: IStyle;
  spinButton: IStyle;
  labelContainer: IStyle;
  horizontalChoiceGroup: IStyle;
  horizontalChoiceGroupContainer: IStyle;
  error: IStyle;
};

export type HRQuerySourceStyleProps = {
  className?: string;
  theme: ITheme;
};

export type HRQuerySourceProps = React.AllHTMLAttributes<HTMLDivElement> & {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<HRQuerySourceStyleProps, HRQuerySourceStyles>;
  source: HRSourcePartSource;
  partId: number;
  onSourceChange: (source: HRSourcePartSource, partId: number) => void;
};