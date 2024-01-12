// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { SourcePartBase } from './SourcePart.base';
import { getStyles } from './SourcePart.styles';
import {
  type SourcePartProps,
  type SourcePartStyleProps,
  type SourcePartStyles,
} from './SourcePart.types';

export const SourcePart: React.FunctionComponent<SourcePartProps> = styled<
  SourcePartProps,
  SourcePartStyleProps,
  SourcePartStyles
>(SourcePartBase, getStyles, undefined, {
  scope: 'SourcePart',
});
