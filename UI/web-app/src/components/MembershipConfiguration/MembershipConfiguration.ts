// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { MembershipConfigurationBase } from './MembershipConfiguration.base';
import { getStyles } from './MembershipConfiguration.styles';
import {
  type MembershipConfigurationProps,
  type MembershipConfigurationStyleProps,
  type MembershipConfigurationStyles,
} from './MembershipConfiguration.types';

export const MembershipConfiguration: React.FunctionComponent<MembershipConfigurationProps> = styled<
  MembershipConfigurationProps,
  MembershipConfigurationStyleProps,
  MembershipConfigurationStyles
>(MembershipConfigurationBase, getStyles, undefined, {
  scope: 'MembershipConfiguration',
});
