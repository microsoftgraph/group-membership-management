// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { RuleCardBase } from './RuleCard.base';
import { getStyles } from './RuleCard.styles';
import {
  type RuleCardProps,
  type RuleCardStyleProps,
  type RuleCardStyles,
} from './RuleCard.types';

export const RuleCard: React.FunctionComponent<RuleCardProps> = styled<
  RuleCardProps,
  RuleCardStyleProps,
  RuleCardStyles
>(RuleCardBase, getStyles, undefined, {
  scope: 'RuleCard',
});
