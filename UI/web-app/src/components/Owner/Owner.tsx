// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { OwnerBase } from './Owner.base';
import { getStyles } from './Owner.styles';
import {
  type IOwnerProps,
  type IOwnerStyleProps,
  type IOwnerStyles,
} from './Owner.types';

export const Owner: React.FunctionComponent<IOwnerProps> = styled<
  IOwnerProps,
  IOwnerStyleProps,
  IOwnerStyles
>(OwnerBase, getStyles, undefined, {
  scope: 'Owner',
});
