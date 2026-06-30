// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { type IStyle, type IStyleFunctionOrObject, type ITheme } from '@fluentui/react';
import { ISourcePart } from '../../models/ISourcePart';

export type RuleCardCarouselStyles = {
  root: IStyle;
  track: IStyle;
  scrollButton: IStyle;
  caret: IStyle;
};

export type RuleCardCarouselStyleProps = {
  className?: string;
  theme: ITheme;
};

export type RuleCardCarouselProps = {
  /**
   * Optional className to apply to the root of the component.
   */
  className?: string;
  /** The rules (source parts) to render as cards. */
  parts: ISourcePart[];
  /** The id of the currently selected rule, if any. */
  selectedPartId?: string;
  /** Invoked with the rule id when a card is selected. */
  onSelectPart: (partId: string) => void;

  /**
   * Whether to render the caret (pointer) beneath the selected card. Defaults to true.
   * Hidden when the selected rule has no reviewable details to point to below the carousel.
   */
  showCaret?: boolean;

  /**
   * Call to provide customized styling that will layer on top of the variant rules.
   */
  styles?: IStyleFunctionOrObject<RuleCardCarouselStyleProps, RuleCardCarouselStyles>;
};
