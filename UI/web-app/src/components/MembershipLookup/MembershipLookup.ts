// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { MembershipLookupBase } from './MembershipLookup.base';
import { getStyles } from './MembershipLookup.styles';
import {
  type MembershipLookupProps,
  type MembershipLookupStyleProps,
  type MembershipLookupStyles,
} from './MembershipLookup.types';

export const MembershipLookup: React.FunctionComponent<MembershipLookupProps> = styled<
  MembershipLookupProps,
  MembershipLookupStyleProps,
  MembershipLookupStyles
>(MembershipLookupBase, getStyles, undefined, {
  scope: 'MembershipLookup',
});
