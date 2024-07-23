// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';

export type OperationStyles = {
  root: IStyle;
  card: IStyle;
  title: IStyle;
};

export type OperationStyleProps = {
  className?: string;
  theme: ITheme;
};

export type OperationProps = React.AllHTMLAttributes<HTMLDivElement> & {
  className?: string;
  styles?: IStyleFunctionOrObject<OperationStyleProps, OperationStyles>;
  title: string;
};