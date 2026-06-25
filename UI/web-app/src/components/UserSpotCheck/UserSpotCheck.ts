// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { UserSpotCheckBase } from './UserSpotCheck.base';
import { getStyles } from './UserSpotCheck.styles';
import {
  type UserSpotCheckProps,
  type UserSpotCheckStyleProps,
  type UserSpotCheckStyles,
} from './UserSpotCheck.types';

export const UserSpotCheck: React.FunctionComponent<UserSpotCheckProps> = styled<
  UserSpotCheckProps,
  UserSpotCheckStyleProps,
  UserSpotCheckStyles
>(UserSpotCheckBase, getStyles, undefined, {
  scope: 'UserSpotCheck',
});
