// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { AdvancedQueryBase } from './AdvancedQuery.base';
import { getStyles } from './AdvancedQuery.styles';
import {
  type IAdvancedQueryProps,
  type IAdvancedQueryStyleProps,
  type IAdvancedQueryStyles,
} from './AdvancedQuery.types';

export const AdvancedQuery: React.FunctionComponent<IAdvancedQueryProps> = styled<
  IAdvancedQueryProps,
  IAdvancedQueryStyleProps,
  IAdvancedQueryStyles
>(AdvancedQueryBase, getStyles, undefined, {
  scope: 'AdvancedQuery',
});

AdvancedQuery.displayName = "StyledAdvancedQueryBase";