// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { HRFilterBase } from './HRFilter.base';
import { getStyles } from './HRFilter.styles';
import {
  type HRFilterProps,
  type HRFilterStyleProps,
  type HRFilterStyles,
} from './HRFilter.types';

export const HRFilter: React.FunctionComponent<HRFilterProps> = styled<
  HRFilterProps,
  HRFilterStyleProps,
  HRFilterStyles
>(HRFilterBase, getStyles, undefined, {
  scope: 'HRFilter',
});