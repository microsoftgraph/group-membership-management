// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { GroupQuerySourceBase } from './GroupQuerySource.base';
import { getStyles } from './GroupQuerySource.styles';
import {
  type GroupQuerySourceProps,
  type GroupQuerySourceStyleProps,
  type GroupQuerySourceStyles,
} from './GroupQuerySource.types';

export const GroupQuerySource: React.FunctionComponent<GroupQuerySourceProps> = styled<
  GroupQuerySourceProps,
  GroupQuerySourceStyleProps,
  GroupQuerySourceStyles
>(GroupQuerySourceBase, getStyles, undefined, {
  scope: 'GroupQuerySource',
});