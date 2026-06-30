// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { RuleCardCarouselBase } from './RuleCardCarousel.base';
import { getStyles } from './RuleCardCarousel.styles';
import {
  type RuleCardCarouselProps,
  type RuleCardCarouselStyleProps,
  type RuleCardCarouselStyles,
} from './RuleCardCarousel.types';

export const RuleCardCarousel: React.FunctionComponent<RuleCardCarouselProps> = styled<
  RuleCardCarouselProps,
  RuleCardCarouselStyleProps,
  RuleCardCarouselStyles
>(RuleCardCarouselBase, getStyles, undefined, {
  scope: 'RuleCardCarousel',
});
