// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { HRQueryItemColumnBase } from './HRQueryItemColumn.base';
import { getStyles } from './HRQueryItemColumn.styles';
import {
  type HRQueryItemColumnProps,
  type HRQueryItemColumnStyleProps,
  type HRQueryItemColumnStyles,
} from './HRQueryItemColumn.types';

export const HRQueryItemColumn: React.FunctionComponent<HRQueryItemColumnProps> = styled<
  HRQueryItemColumnProps,
  HRQueryItemColumnStyleProps,
  HRQueryItemColumnStyles
>(HRQueryItemColumnBase, getStyles, undefined, {
  scope: 'HRQueryItemColumn',
});
