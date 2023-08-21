// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { PagingBarBase } from './PagingBar.base';
import { getStyles } from './PagingBar.styles';
import {
  type IPagingBarProps,
  type IPagingBarStyleProps,
  type IPagingBarStyles,
} from './PagingBar.types';

export const PagingBar: React.FunctionComponent<IPagingBarProps> = styled<
  IPagingBarProps,
  IPagingBarStyleProps,
  IPagingBarStyles
>(PagingBarBase, getStyles, undefined, {
  scope: 'PagingBar',
});
