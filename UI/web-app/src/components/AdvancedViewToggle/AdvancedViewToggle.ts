// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { AdvancedViewToggleBase } from './AdvancedViewToggle.base';
import { getStyles } from './AdvancedViewToggle.styles';
import {
  type AdvancedViewToggleProps,
  type AdvancedViewToggleStyleProps,
  type AdvancedViewToggleStyles,
} from './AdvancedViewToggle.types';

export const AdvancedViewToggle: React.FunctionComponent<AdvancedViewToggleProps> = styled<
  AdvancedViewToggleProps,
  AdvancedViewToggleStyleProps,
  AdvancedViewToggleStyles
>(AdvancedViewToggleBase, getStyles, undefined, {
  scope: 'AdvancedViewToggle',
});
