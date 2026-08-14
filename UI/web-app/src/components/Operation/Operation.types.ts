// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';
import { IOperationsApi } from '../../apis/operations/IOperationsApi';

export type OperationStyles = {
  root: IStyle;
  card: IStyle;
  title: IStyle;
  description: IStyle;
  buttonContainer: IStyle;
  button: IStyle;
};

export type OperationStyleProps = {
  className?: string;
  theme: ITheme;
};

export type OperationProps = React.AllHTMLAttributes<HTMLDivElement> & {
  className?: string;
  styles?: IStyleFunctionOrObject<OperationStyleProps, OperationStyles>;
  title: string;
  description: string;
  buttonText: ButtonTexts;
  /**
   * Which operation actions the card renders. 'service' renders the Stop/Start action,
   * 'reset' renders the Reset action, and 'all' (default) renders both.
   */
  variant?: OperationVariant;
};

export type OperationVariant = 'service' | 'reset' | 'all';

export type ButtonTexts = {
  stop: string;
  stopping: string;
  reset: string;
  resetting: string;
  start: string;
  starting: string;
};
