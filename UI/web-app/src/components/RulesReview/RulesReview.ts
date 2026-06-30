// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { RulesReviewBase } from './RulesReview.base';
import { getStyles } from './RulesReview.styles';
import {
  type RulesReviewProps,
  type RulesReviewStyleProps,
  type RulesReviewStyles,
} from './RulesReview.types';

export const RulesReview: React.FunctionComponent<RulesReviewProps> = styled<
  RulesReviewProps,
  RulesReviewStyleProps,
  RulesReviewStyles
>(RulesReviewBase, getStyles, undefined, {
  scope: 'RulesReview',
});
