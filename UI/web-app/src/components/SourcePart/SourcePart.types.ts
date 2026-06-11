// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import type React from 'react';
import { ISourcePart } from '../../models/ISourcePart';
import { SourcePartQuery } from '../../models/SourcePartQuery';

export type SourcePartStyles = {
  root: IStyle;
  card: IStyle;
  header: IStyle;
  titleTextField: IStyle;
  editButton: IStyle;
  title: IStyle;
  existingTitle: IStyle;
  generatedTitle: IStyle;
  hiddenMembershipIndicator: IStyle;
  hiddenMembershipIcon: IStyle;
  hiddenMembershipText: IStyle;
  expandButton: IStyle;
  content: IStyle;
  controls: IStyle;
  advancedQuery: IStyle;
  deleteButton: IStyle;
  error: IStyle;
  dropdownTitle: IStyle;
  shimmer: IStyle;
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
  partId: string;
  onDelete: (item?: any, partId?: string, ev?: React.FocusEvent<HTMLElement>) => void;
  totalSourceParts: number;
  query: SourcePartQuery;
  part: ISourcePart;
  isNew?: boolean;
  isEditable?: boolean;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<SourcePartStyleProps, SourcePartStyles>;
};
