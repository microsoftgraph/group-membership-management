// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { OperationBase } from './Operation.base';
import { getStyles } from './Operation.styles';
import {
  type OperationProps,
  type OperationStyleProps,
  type OperationStyles,
} from './Operation.types';

export const Operation: React.FunctionComponent<OperationProps> = styled<
  OperationProps,
  OperationStyleProps,
  OperationStyles
>(OperationBase, getStyles, undefined, {
  scope: 'Operation',
});