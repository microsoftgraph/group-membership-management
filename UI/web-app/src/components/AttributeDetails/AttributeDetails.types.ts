// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';

export type AttributeDetailsStyles = {
  root: IStyle;
  close: IStyle;
  modal: IStyle;
  content: IStyle;
  header: IStyle;
  attributeNameHeader: IStyle;
  attributeDescriptionHeader: IStyle;
  attributeRow: IStyle;
  attributeName: IStyle;
  attributeDescription: IStyle;
  headerSeparator: IStyle;
  valueSeparator: IStyle;
};

export type AttributeDetailsStyleProps = {
  className?: string;
  theme: ITheme;
};

export type AttributeDetailsProps = React.AllHTMLAttributes<HTMLDivElement> & {
  className?: string;
  styles?: IStyleFunctionOrObject<AttributeDetailsStyleProps, AttributeDetailsStyles>;
};