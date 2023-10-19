// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { ManageMembershipBase } from './ManageMembership.base';
import { getStyles } from './ManageMembership.styles';
import {
  type IManageMembershipProps,
  type IManageMembershipStyleProps,
  type IManageMembershipStyles,
} from './ManageMembership.types';

export const ManageMembership: React.FunctionComponent<IManageMembershipProps> = styled<
  IManageMembershipProps,
  IManageMembershipStyleProps,
  IManageMembershipStyles
>(ManageMembershipBase, getStyles, undefined, {
  scope: 'ManageMembership',
});
