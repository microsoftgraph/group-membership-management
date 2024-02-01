// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { AdvancedViewSourcePartBase } from './AdvancedViewSourcePart.base';
import { getStyles } from './AdvancedViewSourcePart.styles';
import {
  type IAdvancedViewSourcePartProps,
  type IAdvancedViewSourcePartStyleProps,
  type IAdvancedViewSourcePartStyles,
} from './AdvancedViewSourcePart.types';

export const AdvancedViewSourcePart: React.FunctionComponent<IAdvancedViewSourcePartProps> = styled<
  IAdvancedViewSourcePartProps,
  IAdvancedViewSourcePartStyleProps,
  IAdvancedViewSourcePartStyles
>(AdvancedViewSourcePartBase, getStyles, undefined, {
  scope: 'AdvancedViewSourcePart',
});

AdvancedViewSourcePart.displayName = "StyledAdvancedViewSourcePartBase";