// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';
import { SourcePart } from '../../store/manageMembership.slice';

export type SourcePartStyles = {
  root: IStyle;
  card: IStyle;
  header: IStyle;
  title: IStyle;
  expandButton: IStyle;
  content: IStyle;
  controls: IStyle;
  advancedQuery: IStyle;
  exclusionaryPart: IStyle;
  deleteButton: IStyle;
  error: IStyle;
};

export type SourcePartStyleProps = {
  className?: string;
  theme: ITheme;
};

export type SourcePartProps = React.AllHTMLAttributes<HTMLDivElement> & {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;
  index: number;
  onDelete: (item?: any, index?: number, ev?: React.FocusEvent<HTMLElement>) => void;
  totalSourceParts: number;
  query: string;
  part: SourcePart;
  onQueryChange: (event: React.FormEvent<HTMLTextAreaElement | HTMLInputElement>, newValue?: string) => void;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<SourcePartStyleProps, SourcePartStyles>;
};

export type SourcePartViewProps = SourcePartProps & {
};
