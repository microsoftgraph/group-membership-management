// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { HRQuerySourceBase } from './HRQuerySource.base';
import { getStyles } from './HRQuerySource.styles';
import {
  type HRQuerySourceProps,
  type HRQuerySourceStyleProps,
  type HRQuerySourceStyles,
} from './HRQuerySource.types';

export const HRQuerySource: React.FunctionComponent<HRQuerySourceProps> = styled<
  HRQuerySourceProps,
  HRQuerySourceStyleProps,
  HRQuerySourceStyles
>(HRQuerySourceBase, getStyles, undefined, {
  scope: 'HRQuerySource',
});