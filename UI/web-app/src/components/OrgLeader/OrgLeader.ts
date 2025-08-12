// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { styled } from '@fluentui/react';
import type * as React from 'react';

import { OrgLeaderBase } from './OrgLeader.base';
import { getStyles } from './OrgLeader.styles';
import {
  type OrgLeaderProps,
  type OrgLeaderStyleProps,
  type OrgLeaderStyles,
} from './OrgLeader.types';

export const OrgLeader: React.FunctionComponent<OrgLeaderProps> = styled<
  OrgLeaderProps,
  OrgLeaderStyleProps,
  OrgLeaderStyles
>(OrgLeaderBase, getStyles, undefined, {
  scope: 'OrgLeader',
});
