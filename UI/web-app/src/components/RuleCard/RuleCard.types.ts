// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import { ISourcePart } from '../../models/ISourcePart';

export type RuleCardStyles = {
  root: IStyle;
  selected: IStyle;
  badges: IStyle;
  inclusiveBadge: IStyle;
  exclusiveBadge: IStyle;
  typeBadge: IStyle;
  title: IStyle;
  detailRows: IStyle;
  detailRow: IStyle;
  detailLabel: IStyle;
  detailValue: IStyle;
};

export type RuleCardStyleProps = {
  className?: string;
  theme: ITheme;
  selected?: boolean;
};

export type RuleCardProps = {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;
  /** The rule (source part) to render as a card. */
  part: ISourcePart;
  /** Whether this card is currently selected (highlighted). */
  selected?: boolean;
  /** Invoked with the rule id when the card is selected. */
  onSelect: (partId: string) => void;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<RuleCardStyleProps, RuleCardStyles>;
};
