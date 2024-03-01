// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';
import { ISourcePart } from '../../models/ISourcePart';

export type GroupQuerySourceStyles = {
  groupPicker: IStyle;
};

export type GroupQuerySourceStyleProps = {
  className?: string;
  theme: ITheme;
};

export type GroupQuerySourceProps = React.AllHTMLAttributes<HTMLDivElement> & {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<GroupQuerySourceStyleProps, GroupQuerySourceStyles>;
  part: ISourcePart;
  onSourceChange: (sourceId: string) => void;
};