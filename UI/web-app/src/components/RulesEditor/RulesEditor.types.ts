// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';

export type RulesEditorStyles = {
  root: IStyle;
  headerBar: IStyle;
  description: IStyle;
  headerActions: IStyle;
  addButton: IStyle;
  details: IStyle;
  emptyState: IStyle;
  emptyTitle: IStyle;
  emptyDescription: IStyle;
};

export type RulesEditorStyleProps = {
  className?: string;
  theme: ITheme;
};

export type RulesEditorProps = {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;

  /** Whether the rules can be edited. When false, actions are disabled. Defaults to true. */
  isEditable?: boolean;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<RulesEditorStyleProps, RulesEditorStyles>;
};
