// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import { ISourcePart } from '../../models/ISourcePart';

export type RulesReviewStyles = {
  root: IStyle;
  header: IStyle;
  title: IStyle;
  description: IStyle;
  details: IStyle;
};

export type RulesReviewStyleProps = {
  className?: string;
  theme: ITheme;
};

export type RulesReviewProps = {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;
  /** The rules (source parts) to render as cards. */
  parts: ISourcePart[];
  /** When false, hides the "RULES" header and description. Defaults to true. */
  showHeader?: boolean;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<RulesReviewStyleProps, RulesReviewStyles>;
};
