// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { SelectDestinationBase } from './SelectDestination.base';
import { getStyles } from './SelectDestination.styles';
import {
  type ISelectDestinationProps,
  type ISelectDestinationStyleProps,
  type ISelectDestinationStyles,
} from './SelectDestination.types';

export const SelectDestination: React.FunctionComponent<ISelectDestinationProps> = styled<
  ISelectDestinationProps,
  ISelectDestinationStyleProps,
  ISelectDestinationStyles
>(SelectDestinationBase, getStyles, undefined, {
  scope: 'SelectDestination',
});
